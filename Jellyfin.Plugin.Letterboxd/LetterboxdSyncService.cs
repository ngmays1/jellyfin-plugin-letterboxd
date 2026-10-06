using Jellyfin.Plugin.Letterboxd.Data;
using Jellyfin.Plugin.Letterboxd.Library;
using Jellyfin.Plugin.Letterboxd.Letterboxd;
using Jellyfin.Plugin.Letterboxd.Playlists;
using Jellyfin.Plugin.Letterboxd.Recommendations;
using Jellyfin.Plugin.Letterboxd.Taste;
using Jellyfin.Plugin.Letterboxd.Tmdb;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Letterboxd;

public sealed class LetterboxdSyncService
{
    private readonly LetterboxdRssClient _rssClient;
    private readonly TmdbClient _tmdbClient;
    private readonly SyncStateStore _stateStore;
    private readonly LibraryIndex _libraryIndex;
    private readonly PlaylistSyncService _playlistSyncService;
    private readonly ILogger<LetterboxdSyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LetterboxdSyncService(
        LetterboxdRssClient rssClient,
        TmdbClient tmdbClient,
        SyncStateStore stateStore,
        LibraryIndex libraryIndex,
        PlaylistSyncService playlistSyncService,
        ILogger<LetterboxdSyncService> logger)
    {
        _rssClient = rssClient;
        _tmdbClient = tmdbClient;
        _stateStore = stateStore;
        _libraryIndex = libraryIndex;
        _playlistSyncService = playlistSyncService;
        _logger = logger;
    }

    public bool IsSyncing { get; private set; }

    public async Task<SyncSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new SyncSummary { AlreadyRunning = true };
        }

        IsSyncing = true;
        try
        {
            return await SyncCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Letterboxd sync failed");
            var state = _stateStore.Load();
            state.LastError = ex.Message;
            _stateStore.Save(state);
            return new SyncSummary
            {
                Success = false,
                Error = ex.Message,
                LastSyncUtc = state.LastSyncUtc,
                DiaryCount = state.DiaryCount
            };
        }
        finally
        {
            IsSyncing = false;
            _gate.Release();
        }
    }

    private async Task<SyncSummary> SyncCoreAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            throw new InvalidOperationException("Plugin configuration unavailable");
        }

        var username = config.LetterboxdUsername?.Trim();
        if (string.IsNullOrEmpty(username))
        {
            throw new InvalidOperationException("Letterboxd username is not configured");
        }

        var state = _stateStore.Load();

        // 1. Pull the diary RSS feed.
        var entries = await _rssClient.FetchAsync(username, cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"Letterboxd RSS for '{username}' returned no diary entries");
        }

        // 2. Resolve genres for every rated entry (cached on disk by TmdbClient).
        var genresByTmdbId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var genres = await _tmdbClient.GetMovieGenreIdsAsync(entry.TmdbId, cancellationToken).ConfigureAwait(false);
            if (genres is { Count: > 0 })
            {
                genresByTmdbId[entry.TmdbId] = genres;
            }
        }

        // 3. Build the taste profile and pick seed films.
        var taste = TasteProfileBuilder.Build(entries, genresByTmdbId);
        if (taste.Seeds.Count == 0)
        {
            throw new InvalidOperationException("No rated entries (>= 3.5 stars) available to seed recommendations");
        }

        var genreNames = await _tmdbClient.GetGenreNamesAsync(cancellationToken).ConfigureAwait(false);

        // 4. Gather candidates: TMDB recommendations for each seed + discover by top genres.
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var candidatePool = new Dictionary<string, TmdbMovie>(StringComparer.Ordinal);

        foreach (var seed in taste.Seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!long.TryParse(seed.TmdbId, out var seedId))
            {
                continue;
            }

            var recs = await _tmdbClient.GetRecommendationsAsync(seedId, 1, cancellationToken).ConfigureAwait(false);
            foreach (var movie in recs)
            {
                var key = movie.Id.ToString();
                candidatePool.TryAdd(key, movie);
                occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
            }
        }

        foreach (var genreId in taste.TopGenres)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var discovered = await _tmdbClient.DiscoverByGenreAsync(genreId, 1, cancellationToken).ConfigureAwait(false);
            foreach (var movie in discovered)
            {
                candidatePool.TryAdd(movie.Id.ToString(), movie);
            }
        }

        var watchedIds = entries.Select(e => e.TmdbId).ToHashSet(StringComparer.Ordinal);
        var candidates = candidatePool.Values.Select(movie => new RecommendationService.Candidate(
            movie.Id.ToString(),
            movie.Title,
            ParseYear(movie.ReleaseDate),
            movie.GenreIds ?? new List<int>(),
            movie.PosterPath,
            movie.VoteAverage,
            occurrences.GetValueOrDefault(movie.Id.ToString())));

        var scored = RecommendationService.Score(candidates, taste, watchedIds, genreNames);

        // 5. Split into in-library vs missing.
        _libraryIndex.Rebuild();
        var inLibrary = new List<Recommendation>();
        var missing = new List<Recommendation>();

        foreach (var rec in scored)
        {
            var match = _libraryIndex.FindByTmdbId(rec.TmdbId);
            if (match is not null)
            {
                rec.InLibrary = true;
                rec.ItemId = match.ItemId;
                if (inLibrary.Count < Math.Max(1, config.MaxPlaylistItems))
                {
                    inLibrary.Add(rec);
                }
            }
            else if (missing.Count < Math.Max(1, config.MaxMissingItems))
            {
                missing.Add(rec);
            }
        }

        var recommendations = inLibrary.Concat(missing).ToList();

        // 6. Keep the playlist in sync.
        Guid? playlistId = state.PlaylistId;
        var playlistUpdated = false;
        if (config.EnablePlaylistSync && inLibrary.Count > 0)
        {
            var playlistName = string.IsNullOrWhiteSpace(config.PlaylistName)
                ? "Letterboxd Picks"
                : config.PlaylistName.Trim();
            var ids = inLibrary
                .Where(r => r.ItemId.HasValue)
                .Select(r => r.ItemId!.Value)
                .ToList();

            playlistId = await _playlistSyncService
                .SyncAsync(playlistName, ids, config.PlaylistOwnerUsername, cancellationToken)
                .ConfigureAwait(false);
            playlistUpdated = playlistId.HasValue;
        }

        // 7. Persist.
        state.Entries = entries;
        state.Recommendations = recommendations;
        state.DiaryCount = entries.Count;
        state.LastSyncUtc = DateTime.UtcNow;
        state.LastError = null;
        state.PlaylistId = playlistId;
        if (playlistUpdated)
        {
            state.LastPlaylistUpdateUtc = DateTime.UtcNow;
        }

        _stateStore.Save(state);

        _logger.LogInformation(
            "Letterboxd sync complete: {Entries} entries, {InLibrary} suggestions in library, {Missing} missing, playlist={Playlist}",
            entries.Count, inLibrary.Count, missing.Count, playlistUpdated ? "updated" : "skipped");

        return new SyncSummary
        {
            Success = true,
            LastSyncUtc = state.LastSyncUtc,
            DiaryCount = entries.Count,
            InLibraryCount = inLibrary.Count,
            MissingCount = missing.Count,
            PlaylistUpdated = playlistUpdated,
            PlaylistId = playlistId
        };
    }

    private static int? ParseYear(string? releaseDate)
        => releaseDate is { Length: >= 4 } && int.TryParse(releaseDate[..4], out var year) ? year : null;
}

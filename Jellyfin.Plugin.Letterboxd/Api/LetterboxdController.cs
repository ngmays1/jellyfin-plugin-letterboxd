using Jellyfin.Plugin.Letterboxd.Data;
using Jellyfin.Plugin.Letterboxd.Library;
using Jellyfin.Plugin.Letterboxd.Sidebar;
using Jellyfin.Plugin.Letterboxd.Tmdb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Letterboxd.Api;

[ApiController]
[Authorize]
[Route("LetterboxdPlugin")]
public sealed class LetterboxdController : ControllerBase
{
    private readonly LetterboxdSyncService _syncService;
    private readonly SyncStateStore _stateStore;
    private readonly LibraryIndex _libraryIndex;
    private readonly ILogger<LetterboxdController> _logger;

    public LetterboxdController(
        LetterboxdSyncService syncService,
        SyncStateStore stateStore,
        LibraryIndex libraryIndex,
        ILogger<LetterboxdController> logger)
    {
        _syncService = syncService;
        _stateStore = stateStore;
        _libraryIndex = libraryIndex;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var state = _stateStore.Load();
        var config = Plugin.Instance?.Configuration;
        var username = config?.LetterboxdUsername?.Trim() ?? string.Empty;
        var inLibrary = state.Recommendations.Count(r => r.InLibrary);

        return Ok(new
        {
            configured = username.Length > 0,
            username,
            syncing = _syncService.IsSyncing,
            lastSyncUtc = state.LastSyncUtc,
            lastError = state.LastError,
            diaryCount = state.DiaryCount,
            recommendationCount = state.Recommendations.Count,
            inLibraryCount = inLibrary,
            missingCount = state.Recommendations.Count - inLibrary,
            playlistId = state.PlaylistId,
            playlistUpdatedUtc = state.LastPlaylistUpdateUtc,
            libraryMovieCount = _libraryIndex.IsEmpty ? (int?)null : _libraryIndex.Count
        });
    }

    [HttpGet("reviews")]
    public IActionResult GetReviews()
    {
        var state = _stateStore.Load();
        var reviews = state.Entries.Select(entry =>
        {
            var match = _libraryIndex.FindByTmdbId(entry.TmdbId);
            return new
            {
                title = entry.Title,
                year = entry.Year,
                rating = entry.Rating,
                liked = entry.Liked,
                rewatch = entry.Rewatch,
                watchedDate = entry.WatchedDate,
                tmdbId = entry.TmdbId,
                review = entry.Review,
                link = entry.Link,
                inLibrary = match is not null,
                itemId = match?.ItemId
            };
        });

        return Ok(reviews);
    }

    [HttpGet("recommendations")]
    public IActionResult GetRecommendations([FromQuery] string? filter = null)
    {
        var state = _stateStore.Load();
        IEnumerable<Recommendation> recs = state.Recommendations;

        recs = filter?.ToLowerInvariant() switch
        {
            "inlibrary" => recs.Where(r => r.InLibrary),
            "missing" => recs.Where(r => !r.InLibrary),
            _ => recs
        };

        return Ok(recs.Select(r => new
        {
            tmdbId = r.TmdbId,
            title = r.Title,
            year = r.Year,
            score = r.Score,
            voteAverage = r.VoteAverage,
            posterPath = r.PosterPath,
            posterUrl = r.PosterPath is null ? null : TmdbClient.ImageBaseUrl + r.PosterPath,
            reasons = r.Reasons,
            inLibrary = r.InLibrary,
            itemId = r.ItemId
        }));
    }

    [HttpGet("page")]
    [Produces("text/html")]
    public IActionResult GetSidebarPage()
    {
        var state = _stateStore.Load();
        return Content(SidebarPageRenderer.Render(state), "text/html; charset=utf-8");
    }

    [HttpPost("sync")]
    public IActionResult TriggerSync()
    {
        if (_syncService.IsSyncing)
        {
            return Ok(new { started = false, reason = "already-running" });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _syncService.RunAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background Letterboxd sync failed");
            }
        });

        return Ok(new { started = true });
    }
}

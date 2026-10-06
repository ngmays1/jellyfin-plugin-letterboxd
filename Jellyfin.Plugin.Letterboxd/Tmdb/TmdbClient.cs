using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Letterboxd.Tmdb;

public sealed class TmdbMovie
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("genre_ids")]
    public List<int>? GenreIds { get; set; }

    [JsonPropertyName("genres")]
    public List<TmdbGenre>? Genres { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }
}

public sealed class TmdbGenre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class TmdbPage
{
    [JsonPropertyName("results")]
    public List<TmdbMovie> Results { get; set; } = new();
}

public sealed class TmdbGenreList
{
    [JsonPropertyName("genres")]
    public List<TmdbGenre> Genres { get; set; } = new();
}

public sealed class TmdbCacheData
{
    public Dictionary<string, List<int>> MovieGenres { get; set; } = new();

    public Dictionary<int, string> GenreNames { get; set; } = new();

    public DateTime SavedAtUtc { get; set; }
}

public sealed class TmdbClient
{
    public const string ImageBaseUrl = "https://image.tmdb.org/t/p/w342";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object _lock = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<TmdbClient> _logger;
    private TmdbCacheData? _cache;
    private string? _resolvedApiKey;

    public TmdbClient(IHttpClientFactory httpClientFactory, IApplicationPaths applicationPaths, ILogger<TmdbClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    private string ApiKey
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_resolvedApiKey))
            {
                return _resolvedApiKey;
            }

            var configured = Plugin.Instance?.Configuration.TmdbApiKey?.Trim();
            if (!string.IsNullOrEmpty(configured))
            {
                _resolvedApiKey = configured;
                return _resolvedApiKey;
            }

            _resolvedApiKey = ReadTmdbProviderKey() ?? string.Empty;
            if (string.IsNullOrEmpty(_resolvedApiKey))
            {
                throw new InvalidOperationException("No TMDB API key configured. Set one in the Letterboxd plugin configuration.");
            }

            return _resolvedApiKey;
        }
    }

    public async Task<List<int>?> GetMovieGenreIdsAsync(string tmdbId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            LoadCache_NoLock();
            if (_cache!.MovieGenres.TryGetValue(tmdbId, out var cached))
            {
                return cached;
            }
        }

        if (!long.TryParse(tmdbId, out var numericId))
        {
            return null;
        }

        var movie = await GetAsync<TmdbMovie>($"movie/{numericId}", cancellationToken).ConfigureAwait(false);
        if (movie?.Genres is null || movie.Genres.Count == 0)
        {
            return null;
        }

        var ids = movie.Genres.Select(g => g.Id).ToList();
        lock (_lock)
        {
            LoadCache_NoLock();
            _cache!.MovieGenres[tmdbId] = ids;
            foreach (var genre in movie.Genres)
            {
                _cache.GenreNames[genre.Id] = genre.Name;
            }

            SaveCache_NoLock();
        }

        return ids;
    }

    public async Task<IReadOnlyDictionary<int, string>> GetGenreNamesAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            LoadCache_NoLock();
            if (_cache!.GenreNames.Count > 0)
            {
                return new Dictionary<int, string>(_cache.GenreNames);
            }
        }

        var list = await GetAsync<TmdbGenreList>("genre/movie/list", cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            LoadCache_NoLock();
            foreach (var genre in list?.Genres ?? new List<TmdbGenre>())
            {
                _cache!.GenreNames[genre.Id] = genre.Name;
            }

            SaveCache_NoLock();
            return new Dictionary<int, string>(_cache.GenreNames);
        }
    }

    public Task<List<TmdbMovie>> GetRecommendationsAsync(long movieId, int page, CancellationToken cancellationToken)
        => GetMovieListAsync($"movie/{movieId}/recommendations", page, cancellationToken);

    public async Task<List<TmdbMovie>> DiscoverByGenreAsync(int genreId, int page, CancellationToken cancellationToken)
    {
        var path = $"discover/movie?with_genres={genreId}&sort_by=popularity.desc&vote_count.gte=100";
        var result = await GetAsync<TmdbPage>($"{path}&page={page}", cancellationToken).ConfigureAwait(false);
        return result?.Results ?? new List<TmdbMovie>();
    }

    private async Task<List<TmdbMovie>> GetMovieListAsync(string path, int page, CancellationToken cancellationToken)
    {
        var result = await GetAsync<TmdbPage>($"{path}?page={page}", cancellationToken).ConfigureAwait(false);
        return result?.Results ?? new List<TmdbMovie>();
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        var client = _httpClientFactory.CreateClient("Jellyfin.Plugin.Letterboxd");
        var url = $"https://api.themoviedb.org/3/{path}{(path.Contains('?', StringComparison.Ordinal) ? '&' : '?')}api_key={ApiKey}";

        for (var attempt = 0; ; attempt++)
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                await Task.Delay(110, cancellationToken).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 3)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("TMDB rate limited, backing off {Delay}s", delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"TMDB request failed ({(int)response.StatusCode}) for '{path}': {Truncate(body, 300)}");
        }
    }

    private string? ReadTmdbProviderKey()
    {
        try
        {
            var file = Path.Combine(_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Tmdb.xml");
            if (!File.Exists(file))
            {
                return null;
            }

            var doc = XDocument.Load(file);
            var root = doc.Root;
            var key = root?.Element("TmdbApiKey")?.Value?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                key = root?.Element("ApiKey")?.Value?.Trim();
            }

            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void LoadCache_NoLock()
    {
        if (_cache is not null)
        {
            return;
        }

        try
        {
            var file = CacheFilePath;
            if (File.Exists(file))
            {
                _cache = JsonSerializer.Deserialize<TmdbCacheData>(File.ReadAllText(file), JsonOptions) ?? new TmdbCacheData();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read TMDB cache");
        }

        _cache = new TmdbCacheData();
    }

    private void SaveCache_NoLock()
    {
        try
        {
            if (_cache is null)
            {
                return;
            }

            _cache.SavedAtUtc = DateTime.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
            File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(_cache, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write TMDB cache");
        }
    }

    private string CacheFilePath => Path.Combine(
        Plugin.Instance?.DataFolderPath ?? Path.GetTempPath(),
        "tmdb-cache.json");

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}

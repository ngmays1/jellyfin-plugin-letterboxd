using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Letterboxd.Data;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Letterboxd.Library;

public sealed record LibraryMovie(Guid ItemId, string Name, int? Year);

public sealed class LibraryIndex
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryIndex> _logger;
    private readonly object _lock = new();
    private Dictionary<string, LibraryMovie> _byTmdbId = new(StringComparer.Ordinal);
    private bool _loaded;

    public LibraryIndex(ILibraryManager libraryManager, ILogger<LibraryIndex> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return !_loaded;
            }
        }
    }

    public void Rebuild()
    {
        var map = new Dictionary<string, LibraryMovie>(StringComparer.Ordinal);
        var movies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true
        });

        foreach (var movie in movies)
        {
            if (!TryGetTmdbId(movie, out var tmdbId))
            {
                continue;
            }

            map[tmdbId] = new LibraryMovie(movie.Id, movie.Name, movie.ProductionYear);
        }

        lock (_lock)
        {
            _byTmdbId = map;
            _loaded = true;
        }

        _logger.LogInformation("Indexed {Count} library movies with TMDb ids", map.Count);
    }

    public LibraryMovie? FindByTmdbId(string tmdbId)
    {
        if (!_loaded)
        {
            Rebuild();
        }

        lock (_lock)
        {
            return _byTmdbId.TryGetValue(tmdbId, out var movie) ? movie : null;
        }
    }

    public int Count
    {
        get
        {
            if (!_loaded)
            {
                Rebuild();
            }

            lock (_lock)
            {
                return _byTmdbId.Count;
            }
        }
    }

    private static bool TryGetTmdbId(BaseItem item, out string tmdbId)
    {
        tmdbId = string.Empty;
        foreach (var (key, value) in item.ProviderIds)
        {
            if (string.Equals(key, "Tmdb", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(value))
            {
                tmdbId = value;
                return true;
            }
        }

        return false;
    }
}

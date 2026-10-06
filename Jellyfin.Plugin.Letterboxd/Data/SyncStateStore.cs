using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Letterboxd.Data;

public sealed class SyncStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _lock = new();
    private readonly ILogger<SyncStateStore> _logger;
    private SyncState? _cached;

    public SyncStateStore(ILogger<SyncStateStore> logger)
    {
        _logger = logger;
    }

    public string StateFilePath => Path.Combine(GetDataDirectory(), "sync-state.json");

    public SyncState Load()
    {
        lock (_lock)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            try
            {
                if (File.Exists(StateFilePath))
                {
                    var json = File.ReadAllText(StateFilePath);
                    _cached = JsonSerializer.Deserialize<SyncState>(json, JsonOptions) ?? new SyncState();
                    return _cached;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read Letterboxd sync state, starting fresh");
            }

            _cached = new SyncState();
            return _cached;
        }
    }

    public void Save(SyncState state)
    {
        lock (_lock)
        {
            _cached = state;
            try
            {
                Directory.CreateDirectory(GetDataDirectory());
                var json = JsonSerializer.Serialize(state, JsonOptions);
                var tmp = StateFilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, StateFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist Letterboxd sync state");
            }
        }
    }

    private static string GetDataDirectory()
    {
        var root = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(Path.GetTempPath(), "jellyfin-letterboxd-plugin");
        }

        return root;
    }
}

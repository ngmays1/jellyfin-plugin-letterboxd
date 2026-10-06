using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Letterboxd.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string LetterboxdUsername { get; set; } = string.Empty;

    public string TmdbApiKey { get; set; } = string.Empty;

    public int SyncIntervalHours { get; set; } = 6;

    public string PlaylistName { get; set; } = "Letterboxd Picks";

    public string PlaylistOwnerUsername { get; set; } = string.Empty;

    public bool EnablePlaylistSync { get; set; } = true;

    public int MaxPlaylistItems { get; set; } = 25;

    public int MaxMissingItems { get; set; } = 50;

    public bool EnableSidebarPage { get; set; } = true;
}

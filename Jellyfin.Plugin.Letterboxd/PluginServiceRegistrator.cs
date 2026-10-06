using Jellyfin.Plugin.Letterboxd.Data;
using Jellyfin.Plugin.Letterboxd.Library;
using Jellyfin.Plugin.Letterboxd.Letterboxd;
using Jellyfin.Plugin.Letterboxd.Playlists;
using Jellyfin.Plugin.Letterboxd.Tmdb;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Letterboxd;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SyncStateStore>();
        serviceCollection.AddSingleton<LetterboxdRssClient>();
        serviceCollection.AddSingleton<TmdbClient>();
        serviceCollection.AddSingleton<LibraryIndex>();
        serviceCollection.AddSingleton<PlaylistSyncService>();
        serviceCollection.AddSingleton<LetterboxdSyncService>();
    }
}

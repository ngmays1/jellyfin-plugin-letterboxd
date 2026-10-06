using Jellyfin.Plugin.Letterboxd.Configuration;
using Jellyfin.Plugin.Letterboxd.Sidebar;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Letterboxd;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        SidebarPageRegistrar.EnsureRegistered(applicationPaths, Configuration.EnableSidebarPage);
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => Guid.Parse("9114b8e2-87f8-48f1-9e7b-d7f74302f754");

    public override string Name => "Letterboxd";

    public override string Description => "Letterboxd reviews, ratings and taste-based recommendations for your library.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var prefix = GetType().Namespace!;
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = prefix + ".Configuration.configPage.html"
        };
        yield return new PluginPageInfo
        {
            Name = Name + ".js",
            EmbeddedResourcePath = prefix + ".Configuration.configPage.js"
        };
    }
}

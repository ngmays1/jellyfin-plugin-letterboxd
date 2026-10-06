using System.Text.Json;
using System.Text.Json.Nodes;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Letterboxd.Sidebar;

public static class SidebarPageRegistrar
{
    public const string PageId = "letterboxd";

    public const string PageUrl = "/LetterboxdPlugin/page";

    private const string PluginPagesConfigDir = "Jellyfin.Plugin.PluginPages";

    public static void EnsureRegistered(IApplicationPaths applicationPaths, bool enabled)
    {
        try
        {
            var dir = Path.Combine(applicationPaths.PluginConfigurationsPath, PluginPagesConfigDir);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "config.json");

            JsonObject root;
            if (File.Exists(file))
            {
                try
                {
                    root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    root = new JsonObject();
                }
            }
            else
            {
                root = new JsonObject();
            }

            var pages = root["pages"] as JsonArray ?? new JsonArray();
            for (var i = pages.Count - 1; i >= 0; i--)
            {
                if (pages[i] is JsonObject existing
                    && string.Equals(existing["Id"]?.GetValue<string>(), PageId, StringComparison.OrdinalIgnoreCase))
                {
                    pages.RemoveAt(i);
                }
            }

            if (enabled)
            {
                pages.Add(new JsonObject
                {
                    ["Id"] = PageId,
                    ["Url"] = PageUrl,
                    ["DisplayText"] = "Letterboxd",
                    ["Icon"] = "local_movies"
                });
            }

            root["pages"] = pages;
            File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort: Plugin Pages may not be installed. The sidebar entry simply won't appear.
        }
    }
}
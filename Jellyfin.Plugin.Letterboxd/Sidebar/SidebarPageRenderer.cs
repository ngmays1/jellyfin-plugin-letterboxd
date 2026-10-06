using System.Net;
using System.Text;
using Jellyfin.Plugin.Letterboxd.Data;
using Jellyfin.Plugin.Letterboxd.Tmdb;

namespace Jellyfin.Plugin.Letterboxd.Sidebar;

public static class SidebarPageRenderer
{
    public static string Render(SyncState state)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"letterboxdPage padded-left padded-right padded-bottom-page\">");

        sb.Append("<style>");
        sb.Append(".letterboxdPage h2 { margin-top:1.5em; }");
        sb.Append(".letterboxdGrid { display:grid; grid-template-columns:repeat(auto-fill,minmax(150px,1fr)); gap:16px; }");
        sb.Append(".letterboxdCard { background:rgba(255,255,255,0.05); border-radius:8px; overflow:hidden; }");
        sb.Append(".letterboxdCard img { width:100%; display:block; }");
        sb.Append(".letterboxdCard .meta { padding:8px 10px; }");
        sb.Append(".letterboxdCard .title { font-weight:600; }");
        sb.Append(".letterboxdCard .reason { opacity:0.7; font-size:0.85em; }");
        sb.Append(".letterboxdReview { border-left:3px solid #00c030; padding:6px 12px; margin:12px 0; }");
        sb.Append(".letterboxdReview .title { font-weight:600; }");
        sb.Append(".letterboxdStars { color:#00c030; }");
        sb.Append(".letterboxdBadge { font-size:0.8em; opacity:0.75; margin-left:8px; }");
        sb.Append(".letterboxdMuted { opacity:0.7; }");
        sb.Append("</style>");

        sb.Append("<h1>Letterboxd</h1>");
        sb.Append("<p class=\"letterboxdMuted\">");
        if (state.LastSyncUtc is not null)
        {
            sb.Append($"Last synced {state.LastSyncUtc.Value:yyyy-MM-dd HH:mm} UTC &middot; {state.Entries.Count} diary entries &middot; ");
            sb.Append($"{state.Recommendations.Count(r => r.InLibrary)} in library &middot; {state.Recommendations.Count(r => !r.InLibrary)} to add.");
        }
        else
        {
            sb.Append("No sync has run yet. Open the Letterboxd plugin settings and press Sync, or wait for the scheduled task.");
        }

        sb.Append("</p>");

        RenderRecommendations(sb, "In your library", state.Recommendations.Where(r => r.InLibrary).ToList());
        RenderRecommendations(sb, "Worth adding", state.Recommendations.Where(r => !r.InLibrary).ToList());
        RenderReviews(sb, state.Entries);

        sb.Append("</div>");
        return sb.ToString();
    }

    private static void RenderRecommendations(StringBuilder sb, string heading, List<Recommendation> recs)
    {
        if (recs.Count == 0)
        {
            return;
        }

        sb.Append($"<h2>{Encode(heading)}</h2>");
        sb.Append("<div class=\"letterboxdGrid\">");
        foreach (var rec in recs)
        {
            var title = WebUtility.HtmlEncode(rec.Year is null ? rec.Title : $"{rec.Title} ({rec.Year})");
            var reason = WebUtility.HtmlEncode(rec.Reasons.Count > 0 ? rec.Reasons[0] : string.Empty);
            var poster = rec.PosterPath is null ? string.Empty : TmdbClient.ImageBaseUrl + rec.PosterPath;

            if (rec.InLibrary && rec.ItemId is not null)
            {
                sb.Append($"<a class=\"letterboxdCard\" href=\"#/details?id={rec.ItemId.Value:N}\">");
            }
            else
            {
                sb.Append("<div class=\"letterboxdCard\">");
            }

            if (!string.IsNullOrEmpty(poster))
            {
                sb.Append($"<img loading=\"lazy\" src=\"{WebUtility.HtmlEncode(poster)}\" alt=\"\" />");
            }

            sb.Append("<div class=\"meta\">");
            sb.Append($"<div class=\"title\">{title}</div>");
            if (!string.IsNullOrEmpty(reason))
            {
                sb.Append($"<div class=\"reason\">{reason}</div>");
            }

            sb.Append("</div>");
            sb.Append(rec.InLibrary && rec.ItemId is not null ? "</a>" : "</div>");
        }

        sb.Append("</div>");
    }

    private static void RenderReviews(StringBuilder sb, List<DiaryEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        sb.Append("<h2>Your recent reviews</h2>");
        foreach (var entry in entries)
        {
            sb.Append("<div class=\"letterboxdReview\">");
            var title = WebUtility.HtmlEncode(entry.Year is null ? entry.Title : $"{entry.Title} ({entry.Year})");
            sb.Append($"<div class=\"title\">{title}");
            if (entry.Rating is not null)
            {
                sb.Append($" <span class=\"letterboxdStars\">{Stars(entry.Rating.Value)}</span>");
            }

            if (!string.IsNullOrWhiteSpace(entry.Link))
            {
                sb.Append($" <a class=\"letterboxdBadge\" href=\"{WebUtility.HtmlEncode(entry.Link)}\" target=\"_blank\" rel=\"noopener\">view on Letterboxd</a>");
            }

            sb.Append("</div>");

            if (!string.IsNullOrWhiteSpace(entry.Review))
            {
                var text = entry.Review.Length > 600 ? entry.Review[..600] + "…" : entry.Review;
                sb.Append($"<div>{Encode(text)}</div>");
            }

            sb.Append("</div>");
        }
    }

    private static string Stars(double rating)
    {
        var full = (int)Math.Floor(rating);
        var half = rating - full >= 0.5;
        var text = new string('★', Math.Clamp(full, 0, 5));
        if (half)
        {
            text += "½";
        }

        return text;
    }

    private static string Encode(string value)
        => WebUtility.HtmlEncode(value).Replace("\n", "<br/>");
}
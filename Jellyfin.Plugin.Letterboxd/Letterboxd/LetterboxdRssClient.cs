using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Jellyfin.Plugin.Letterboxd.Data;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Letterboxd.Letterboxd;

public sealed partial class LetterboxdRssClient
{
    private const string LetterboxdNs = "https://letterboxd.com";
    private const string TmdbNs = "https://themoviedb.org";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LetterboxdRssClient> _logger;

    public LetterboxdRssClient(IHttpClientFactory httpClientFactory, ILogger<LetterboxdRssClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<DiaryEntry>> FetchAsync(string username, CancellationToken cancellationToken)
    {
        var url = $"https://letterboxd.com/{Uri.EscapeDataString(username)}/rss/";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Jellyfin-LetterboxdPlugin/12.1.0 (homelab)");

        using var response = await _httpClientFactory
            .CreateClient("Jellyfin.Plugin.Letterboxd")
            .SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Letterboxd RSS returned HTTP {(int)response.StatusCode} for user '{username}'");
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var entries = Parse(xml);
        _logger.LogInformation("Fetched {Count} diary entries from Letterboxd RSS for {Username}", entries.Count, username);
        return entries;
    }

    public static List<DiaryEntry> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var entries = new List<DiaryEntry>();

        foreach (var item in doc.Descendants("item"))
        {
            var tmdbId = item.Element(XName.Get("movieId", TmdbNs))?.Value?.Trim();
            var title = item.Element(XName.Get("filmTitle", LetterboxdNs))?.Value?.Trim();
            if (string.IsNullOrEmpty(tmdbId) || string.IsNullOrEmpty(title))
            {
                continue;
            }

            var entry = new DiaryEntry
            {
                Title = title,
                TmdbId = tmdbId,
                Year = int.TryParse(item.Element(XName.Get("filmYear", LetterboxdNs))?.Value, out var year) ? year : null,
                Rating = double.TryParse(
                    item.Element(XName.Get("memberRating", LetterboxdNs))?.Value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var rating) ? rating : null,
                Liked = string.Equals(item.Element(XName.Get("memberLike", LetterboxdNs))?.Value, "Yes", StringComparison.OrdinalIgnoreCase),
                Rewatch = string.Equals(item.Element(XName.Get("rewatch", LetterboxdNs))?.Value, "Yes", StringComparison.OrdinalIgnoreCase),
                WatchedDate = item.Element(XName.Get("watchedDate", LetterboxdNs))?.Value?.Trim(),
                Link = item.Element("link")?.Value?.Trim() ?? string.Empty,
                Review = StripHtml(item.Element("description")?.Value)
            };

            entries.Add(entry);
        }

        return entries;
    }

    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var s = html;
        s = ImgRegex().Replace(s, string.Empty);
        s = ClosingParagraphRegex().Replace(s, "\n");
        s = BreakRegex().Replace(s, "\n");
        s = TagRegex().Replace(s, string.Empty);
        s = WebUtility.HtmlDecode(s);

        var lines = s.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lines).Trim();
    }

    [GeneratedRegex("<img[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRegex();

    [GeneratedRegex("</p\\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ClosingParagraphRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();
}

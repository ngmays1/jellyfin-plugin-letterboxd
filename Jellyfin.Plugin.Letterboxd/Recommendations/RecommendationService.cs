using Jellyfin.Plugin.Letterboxd.Data;
using Jellyfin.Plugin.Letterboxd.Taste;
using Jellyfin.Plugin.Letterboxd.Tmdb;

namespace Jellyfin.Plugin.Letterboxd.Recommendations;

public static class RecommendationService
{
    public static List<Recommendation> Score(
        IEnumerable<Candidate> candidates,
        TasteProfile taste,
        IReadOnlySet<string> excludeTmdbIds,
        IReadOnlyDictionary<int, string> genreNames)
    {
        var best = new Dictionary<string, Recommendation>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (excludeTmdbIds.Contains(candidate.TmdbId) || string.IsNullOrEmpty(candidate.TmdbId))
            {
                continue;
            }

            var genreScore = 0.0;
            var matchedGenres = new List<string>();
            foreach (var genreId in candidate.GenreIds)
            {
                if (!taste.GenreWeights.TryGetValue(genreId, out var weight) || weight <= 0)
                {
                    continue;
                }

                genreScore += weight;
                if (genreNames.TryGetValue(genreId, out var name))
                {
                    matchedGenres.Add(name);
                }
            }

            var overlap = taste.PositiveWeight > 0 ? genreScore / taste.PositiveWeight : 0;
            var recurrence = 0.12 * Math.Min(candidate.Occurrences, 5) / 5.0;
            var score = overlap + recurrence;
            if (score <= 0)
            {
                continue;
            }

            var reasons = new List<string>();
            if (matchedGenres.Count > 0)
            {
                reasons.Add("Because you like " + string.Join(", ", matchedGenres.Take(2)));
            }

            if (candidate.Occurrences > 0)
            {
                reasons.Add(candidate.Occurrences == 1
                    ? "Recommended for one of your favorites"
                    : $"Recommended for {candidate.Occurrences} of your favorites");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("Popular in your favorite genres");
            }

            var rec = new Recommendation
            {
                TmdbId = candidate.TmdbId,
                Title = candidate.Title,
                Year = candidate.Year,
                Score = Math.Round(score, 4),
                VoteAverage = candidate.VoteAverage,
                PosterPath = candidate.PosterPath,
                Reasons = reasons
            };

            if (!best.TryGetValue(rec.TmdbId, out var existing) || rec.Score > existing.Score)
            {
                best[rec.TmdbId] = rec;
            }
            else if (existing.Reasons.Count == 1 && rec.Reasons.Count > 1)
            {
                existing.Reasons = rec.Reasons;
            }
        }

        return best.Values
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.VoteAverage)
            .ToList();
    }

    public sealed record Candidate(
        string TmdbId,
        string Title,
        int? Year,
        List<int> GenreIds,
        string? PosterPath,
        double VoteAverage,
        int Occurrences);
}

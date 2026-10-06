using Jellyfin.Plugin.Letterboxd.Data;

namespace Jellyfin.Plugin.Letterboxd.Taste;

public sealed class TasteProfile
{
    public Dictionary<int, double> GenreWeights { get; init; } = new();

    public double PositiveWeight { get; init; }

    public List<int> TopGenres { get; init; } = new();

    public List<SeedFilm> Seeds { get; init; } = new();

    public sealed record SeedFilm(string TmdbId, string Title, double Rating, int Order);
}

public static class TasteProfileBuilder
{
    public const double NeutralRating = 2.5;
    public const double SeedThreshold = 3.5;
    public const int MaxSeeds = 10;
    public const int MaxTopGenres = 5;

    public static TasteProfile Build(
        IReadOnlyList<DiaryEntry> entries,
        IReadOnlyDictionary<string, List<int>> genresByTmdbId)
    {
        var weights = new Dictionary<int, double>();
        var positive = 0.0;
        var seedCandidates = new List<TasteProfile.SeedFilm>();

        for (var order = 0; order < entries.Count; order++)
        {
            var entry = entries[order];
            if (entry.Rating is null)
            {
                continue;
            }

            var weight = entry.Rating.Value - NeutralRating;
            if (genresByTmdbId.TryGetValue(entry.TmdbId, out var genres))
            {
                foreach (var genreId in genres)
                {
                    weights.TryGetValue(genreId, out var current);
                    weights[genreId] = current + weight;
                }
            }

            if (entry.Rating.Value >= SeedThreshold)
            {
                seedCandidates.Add(new TasteProfile.SeedFilm(entry.TmdbId, entry.Title, entry.Rating.Value, order));
            }
        }

        foreach (var (genreId, value) in weights)
        {
            if (value > 0)
            {
                positive += value;
            }
        }

        var seeds = seedCandidates
            .OrderByDescending(s => s.Rating)
            .ThenByDescending(s => s.Order)
            .Take(MaxSeeds)
            .ToList();

        var topGenres = weights
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(MaxTopGenres)
            .Select(kv => kv.Key)
            .ToList();

        return new TasteProfile
        {
            GenreWeights = weights,
            PositiveWeight = positive,
            TopGenres = topGenres,
            Seeds = seeds
        };
    }
}

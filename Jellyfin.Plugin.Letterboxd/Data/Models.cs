namespace Jellyfin.Plugin.Letterboxd.Data;

public sealed class DiaryEntry
{
    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public double? Rating { get; set; }

    public bool Liked { get; set; }

    public bool Rewatch { get; set; }

    public string? WatchedDate { get; set; }

    public string TmdbId { get; set; } = string.Empty;

    public string Review { get; set; } = string.Empty;

    public string Link { get; set; } = string.Empty;
}

public sealed class Recommendation
{
    public string TmdbId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public double Score { get; set; }

    public double VoteAverage { get; set; }

    public string? PosterPath { get; set; }

    public List<string> Reasons { get; set; } = new();

    public bool InLibrary { get; set; }

    public Guid? ItemId { get; set; }
}

public sealed class SyncState
{
    public DateTime? LastSyncUtc { get; set; }

    public string? LastError { get; set; }

    public int DiaryCount { get; set; }

    public DateTime? LastPlaylistUpdateUtc { get; set; }

    public Guid? PlaylistId { get; set; }

    public List<DiaryEntry> Entries { get; set; } = new();

    public List<Recommendation> Recommendations { get; set; } = new();
}

public sealed class SyncSummary
{
    public bool Success { get; set; }

    public bool AlreadyRunning { get; set; }

    public string? Error { get; set; }

    public DateTime? LastSyncUtc { get; set; }

    public int DiaryCount { get; set; }

    public int InLibraryCount { get; set; }

    public int MissingCount { get; set; }

    public bool PlaylistUpdated { get; set; }

    public Guid? PlaylistId { get; set; }
}

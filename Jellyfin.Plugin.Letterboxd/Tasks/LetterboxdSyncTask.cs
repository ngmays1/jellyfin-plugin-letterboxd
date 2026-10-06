using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Letterboxd.Tasks;

public sealed class LetterboxdSyncTask : IScheduledTask
{
    private readonly LetterboxdSyncService _syncService;
    private readonly ILogger<LetterboxdSyncTask> _logger;

    public LetterboxdSyncTask(LetterboxdSyncService syncService, ILogger<LetterboxdSyncTask> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public string Name => "Sync Letterboxd data";

    public string Key => "LetterboxdSync";

    public string Description => "Fetches your Letterboxd diary RSS feed, rebuilds taste-based recommendations, and updates the Letterboxd playlist.";

    public string Category => "Letterboxd";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        var summary = await _syncService.RunAsync(cancellationToken).ConfigureAwait(false);
        if (!summary.Success && !summary.AlreadyRunning)
        {
            _logger.LogWarning("Letterboxd sync task finished with error: {Error}", summary.Error);
        }

        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var hours = Plugin.Instance?.Configuration.SyncIntervalHours ?? 6;
        if (hours < 1)
        {
            hours = 6;
        }

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(hours).Ticks
        };
    }
}

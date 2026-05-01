using YtDlpGui.Models;

namespace YtDlpGui.Services;

public sealed record DownloadFinalResult(DownloadStatus FinalStatus, int ExitCode, string? Reason);

public interface IDownloadQueue
{
    /// <summary>Max number of concurrent yt-dlp processes. Default 2 (SPEC §5.1).</summary>
    int Parallelism { get; set; }

    void Enqueue(DownloadItem item);

    /// <summary>True if this item currently holds an active process / waiting on the gate.</summary>
    bool IsInFlight(Guid id);

    /// <summary>Cancels an in-flight item (kills the process tree). No-op if already terminal.</summary>
    void CancelItem(Guid id);

    /// <summary>Cancels all in-flight items.</summary>
    void CancelAll();

    event Action<DownloadItem, DownloadStatus>? StatusChanged;
    event Action<DownloadItem, ProgressSnapshot>? ProgressUpdated;
    event Action<DownloadItem, string>? LogLine;
    event Action<DownloadItem, DownloadFinalResult>? Finished;
}

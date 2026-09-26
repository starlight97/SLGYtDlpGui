namespace YtDlpGui.Models;

/// <summary>
/// A single queued URL. The view-model wraps this and projects it to the UI;
/// the queue mutates it. All mutations happen on the UI thread (the queue
/// marshals via Dispatcher).
/// </summary>
public sealed class DownloadItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string Url { get; init; }
    /// <summary>Snapshot of options at the moment this item was enqueued.</summary>
    public required DownloadOptions Options { get; init; }
    public DateTime EnqueuedAtUtc { get; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>
    /// The final file path captured from yt-dlp's <c>--print after_move:</c> hook.
    /// Set just before completion; null if the run failed before the move stage.
    /// Consumed by <c>DownloadItemViewModel.OpenFile</c>.
    /// </summary>
    public string? FinalFilePath { get; set; }
}

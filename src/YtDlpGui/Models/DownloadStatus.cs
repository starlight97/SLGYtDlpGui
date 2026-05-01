namespace YtDlpGui.Models;

public enum DownloadStatus
{
    Queued,
    Resolving,
    Downloading,
    Postprocessing,
    Done,
    Failed,
    Canceled,
}

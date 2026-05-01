namespace YtDlpGui.Models;

/// <summary>
/// One parsed PROGRESS: line from yt-dlp. All fields nullable because yt-dlp
/// fills 'NA' for unknowns; ProgressParser turns those into nulls.
/// </summary>
public readonly record struct ProgressSnapshot(
    double? Percent,
    long? DownloadedBytes,
    long? TotalBytes,
    double? SpeedBytesPerSec,
    double? EtaSeconds,
    int? FragmentIndex,
    int? FragmentCount);

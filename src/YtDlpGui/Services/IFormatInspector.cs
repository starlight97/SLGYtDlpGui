using YtDlpGui.Models;

namespace YtDlpGui.Services;

public sealed record FormatInspectionResult(
    string? Title,
    long? DurationSeconds,
    IReadOnlyList<FormatInfo> Formats);

public interface IFormatInspector
{
    /// <summary>
    /// Runs <c>yt-dlp -F --dump-single-json --skip-download --no-playlist</c> against
    /// the given URL and parses the JSON. Throws on cancellation, missing yt-dlp,
    /// non-zero exit, or unparseable output.
    /// </summary>
    Task<FormatInspectionResult> InspectAsync(string url, CancellationToken ct);
}

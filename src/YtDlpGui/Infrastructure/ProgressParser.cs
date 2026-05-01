using System.Globalization;
using YtDlpGui.Models;

namespace YtDlpGui.Infrastructure;

/// <summary>
/// Parses yt-dlp output lines under the structured contract from SPEC §6:
///  - <c>PROGRESS:</c> prefix → machine-readable progress snapshot
///  - <c>[stage]</c> brackets → coarse status transitions
/// Other lines fall through to the per-item log unchanged.
/// </summary>
public static class ProgressParser
{
    public const string ProgressPrefix = "PROGRESS:";

    /// <summary>
    /// Try to parse a structured PROGRESS line. The template (see <see cref="ArgBuilder.ProgressTemplate"/>)
    /// is "PROGRESS:percent|downloaded|total|speed|eta|fragIdx|fragCount". yt-dlp writes "NA"
    /// for unknown numeric fields; those become null.
    /// </summary>
    public static bool TryParseProgress(string line, out ProgressSnapshot snapshot)
    {
        snapshot = default;
        if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal)) return false;

        var payload = line.AsSpan(ProgressPrefix.Length);
        Span<Range> ranges = stackalloc Range[8];
        var n = payload.Split(ranges, '|');
        if (n < 7) return false;

        snapshot = new ProgressSnapshot(
            Percent: ParsePercent(payload[ranges[0]]),
            DownloadedBytes: ParseLong(payload[ranges[1]]),
            TotalBytes: ParseLong(payload[ranges[2]]),
            SpeedBytesPerSec: ParseDouble(payload[ranges[3]]),
            EtaSeconds: ParseDouble(payload[ranges[4]]),
            FragmentIndex: ParseInt(payload[ranges[5]]),
            FragmentCount: ParseInt(payload[ranges[6]]));
        return true;
    }

    /// <summary>
    /// Coarse status transitions inferred from yt-dlp's bracketed stage tags.
    /// Returns null if the line doesn't carry a useful stage signal.
    /// </summary>
    public static DownloadStatus? TryInferStatus(string line)
    {
        // Very small set; this isn't documented contract, just stable observed behavior.
        if (line.StartsWith("[info]", StringComparison.Ordinal)) return DownloadStatus.Resolving;
        if (line.StartsWith("[youtube]", StringComparison.Ordinal)) return DownloadStatus.Resolving;
        if (line.StartsWith("[generic]", StringComparison.Ordinal)) return DownloadStatus.Resolving;
        if (line.StartsWith("[download]", StringComparison.Ordinal)) return DownloadStatus.Downloading;
        if (line.StartsWith("[Merger]", StringComparison.Ordinal)) return DownloadStatus.Postprocessing;
        if (line.StartsWith("[ffmpeg]", StringComparison.Ordinal)) return DownloadStatus.Postprocessing;
        if (line.StartsWith("[Metadata]", StringComparison.Ordinal)) return DownloadStatus.Postprocessing;
        if (line.StartsWith("[ThumbnailsConvertor]", StringComparison.Ordinal)) return DownloadStatus.Postprocessing;
        if (line.StartsWith("[EmbedThumbnail]", StringComparison.Ordinal)) return DownloadStatus.Postprocessing;
        if (line.StartsWith("[ExtractAudio]", StringComparison.Ordinal)) return DownloadStatus.Postprocessing;
        if (line.StartsWith("[VideoConvertor]", StringComparison.Ordinal)) return DownloadStatus.Postprocessing;
        if (line.StartsWith("[FixupM3u8]", StringComparison.Ordinal)) return DownloadStatus.Postprocessing;
        return null;
    }

    private static double? ParsePercent(ReadOnlySpan<char> s)
    {
        // "_percent_str" comes pre-formatted like " 12.3%" or "100.0%".
        var trimmed = s.Trim();
        if (trimmed.IsEmpty || trimmed.SequenceEqual("NA")) return null;
        if (trimmed[^1] == '%') trimmed = trimmed[..^1];
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    private static long? ParseLong(ReadOnlySpan<char> s)
    {
        var t = s.Trim();
        if (t.IsEmpty || t.SequenceEqual("NA")) return null;
        return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? ParseInt(ReadOnlySpan<char> s)
    {
        var t = s.Trim();
        if (t.IsEmpty || t.SequenceEqual("NA")) return null;
        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static double? ParseDouble(ReadOnlySpan<char> s)
    {
        var t = s.Trim();
        if (t.IsEmpty || t.SequenceEqual("NA")) return null;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace YtDlpGui.Infrastructure;

/// <summary>
/// Pure helpers for yt-dlp's date-based version strings (e.g. "2026.03.17", or
/// nightly "2026.08.19.232323"). No I/O here — callers pass in the process output
/// and "today" so this stays trivially unit-testable.
/// </summary>
public static class YtDlpVersionParser
{
    /// <summary>yt-dlp itself warns "older than 90 days" past this age; we mirror the same bar.</summary>
    public const int OutdatedAfterDays = 90;

    // "2026.03.17" (stable) or "2026.08.19.232323" (nightly build suffix). Anchored to the
    // whole (trimmed) line so noise like "v2026.03.17" or "2026.03.17 extra" doesn't match.
    private static readonly Regex VersionRegex =
        new(@"^([0-9]{4}\.[0-9]{2}\.[0-9]{2})(?:\.[0-9]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "WARNING: Your yt-dlp version (2026.03.17) is older than 90 days!"
    private static readonly Regex OutdatedWarningRegex =
        new(@"Your yt-dlp version \(([^)\s]+)\) is older than [0-9]+ days", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Scans <paramref name="output"/> line-by-line for a yt-dlp version string.
    /// Returns the first line that parses; <paramref name="version"/> keeps the full
    /// matched text (nightly suffix included), <paramref name="releaseDate"/> is the
    /// yyyy-MM-dd portion.
    /// </summary>
    public static bool TryParse(string? output, out string version, out DateOnly releaseDate)
    {
        version = string.Empty;
        releaseDate = default;
        if (string.IsNullOrWhiteSpace(output)) return false;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var m = VersionRegex.Match(line);
            if (!m.Success) continue;

            if (!DateOnly.TryParseExact(
                    m.Groups[1].Value, "yyyy.MM.dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue; // e.g. "2026.02.30" / "2026.13.01" — not a real date, keep scanning.
            }

            version = line;
            releaseDate = date;
            return true;
        }

        return false;
    }

    /// <summary>Days between <paramref name="releaseDate"/> and <paramref name="today"/>. Can be negative.</summary>
    public static int AgeInDays(DateOnly releaseDate, DateOnly today) => today.DayNumber - releaseDate.DayNumber;

    /// <summary>Same threshold as yt-dlp's own "older than 90 days" warning.</summary>
    public static bool IsOutdated(DateOnly releaseDate, DateOnly today) => AgeInDays(releaseDate, today) > OutdatedAfterDays;

    /// <summary>
    /// Fallback path: detect yt-dlp's own "is older than 90 days" warning line during a
    /// download run (used when the startup <c>--version</c> check couldn't run at all).
    /// </summary>
    public static bool TryParseOutdatedWarning(string? line, out string version)
    {
        version = string.Empty;
        if (line is null || !line.Contains("is older than", StringComparison.Ordinal)) return false;

        var m = OutdatedWarningRegex.Match(line);
        if (!m.Success) return false;

        version = m.Groups[1].Value;
        return true;
    }
}

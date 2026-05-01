namespace YtDlpGui.Models;

/// <summary>
/// One row from <c>yt-dlp -F --dump-single-json</c>. SPEC §5.4: the inspector
/// parses the JSON output rather than scraping the human-readable table.
/// All fields are nullable / defaulted because yt-dlp omits unknowns rather
/// than emitting a sentinel.
/// </summary>
public sealed class FormatInfo
{
    public string Id { get; init; } = string.Empty;
    public string Ext { get; init; } = string.Empty;
    public string Resolution { get; init; } = string.Empty;
    public double? Fps { get; init; }
    public string Vcodec { get; init; } = string.Empty;
    public string Acodec { get; init; } = string.Empty;
    /// <summary>Bytes. Falls back to <c>filesize_approx</c> when exact size is unknown.</summary>
    public long? Filesize { get; init; }
    /// <summary>Total bitrate in kbit/s. Used as the ranking signal for "Best video / audio".</summary>
    public double? Tbr { get; init; }
    public string Note { get; init; } = string.Empty;
    /// <summary>Container/protocol hint, e.g. "https", "m3u8_native", "dash".</summary>
    public string Protocol { get; init; } = string.Empty;

    public bool HasVideo => !string.IsNullOrEmpty(Vcodec) && Vcodec != "none";
    public bool HasAudio => !string.IsNullOrEmpty(Acodec) && Acodec != "none";
    public bool IsAudioOnly => !HasVideo && HasAudio;
    public bool IsVideoOnly => HasVideo && !HasAudio;
    public bool IsCombined => HasVideo && HasAudio;

    public string FilesizeText
    {
        get
        {
            if (Filesize is not long b || b <= 0) return string.Empty;
            return b switch
            {
                >= 1_000_000_000 => $"{b / 1_000_000_000.0:F2} GB",
                >= 1_000_000 => $"{b / 1_000_000.0:F1} MB",
                >= 1_000 => $"{b / 1_000.0:F1} KB",
                _ => $"{b} B",
            };
        }
    }

    public string TbrText => Tbr is double v && v > 0 ? $"{v:F0}k" : string.Empty;
    public string FpsText => Fps is double v && v > 0 ? v.ToString("F0") : string.Empty;
}

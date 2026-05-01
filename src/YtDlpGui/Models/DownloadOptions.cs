namespace YtDlpGui.Models;

/// <summary>
/// Serializable option set for a single yt-dlp run. Mirrors the option groups in
/// SPEC §5.3. Empty / default values are not emitted by ArgBuilder, so the final
/// command line stays short and inspectable (§7).
///
/// v0.2 binds a subset of these to the Options panel; the rest exist now so that
/// v0.3+ can wire up more UI without revisiting the model.
/// </summary>
public sealed class DownloadOptions
{
    // --- Output ---
    public string OutputFolder { get; set; } = string.Empty;
    public string OutputTemplate { get; set; } = "%(title)s.%(ext)s";
    public bool RestrictFilenames { get; set; }
    public bool WindowsFilenames { get; set; }
    /// <summary>SPEC §8: post-process Hangul fix. Default ON; toggle per §13.</summary>
    public bool NfcNormalize { get; set; } = true;

    // --- Format ---
    /// <summary>Raw -f selector. v0.2 only supports raw; preset/inspector come in v0.3+.</summary>
    public string FormatSelector { get; set; } =
        "bv*[ext=mp4][vcodec^=avc]+ba[ext=m4a]/bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/b";

    // --- Container / postprocessing ---
    /// <summary>"mp4" | "mkv" | "webm" | "" (auto).</summary>
    public string MergeOutputFormat { get; set; } = "mp4";
    public bool EmbedMetadata { get; set; } = true;
    public bool EmbedThumbnail { get; set; } = true;
    public bool EmbedChapters { get; set; }
    public bool WriteDescription { get; set; }
    public bool WriteInfoJson { get; set; }

    // --- Subtitles ---
    public bool WriteSubs { get; set; }
    public bool WriteAutoSubs { get; set; }
    public bool EmbedSubs { get; set; }
    /// <summary>Comma-separated, e.g. "ko,en,en.*".</summary>
    public string SubLangs { get; set; } = string.Empty;
    /// <summary>"" | "srt" | "vtt".</summary>
    public string ConvertSubsTo { get; set; } = string.Empty;

    // --- Audio extraction ---
    public bool ExtractAudio { get; set; }
    /// <summary>"" | "mp3" | "m4a" | "opus" | "vorbis" | "wav" | "flac" | "best".</summary>
    public string AudioCodec { get; set; } = string.Empty;
    /// <summary>0..9 vbr or kbps as string; empty = yt-dlp default.</summary>
    public string AudioQuality { get; set; } = string.Empty;
    public bool KeepVideo { get; set; }

    // --- Network / auth ---
    /// <summary>"" | "chrome" | "firefox" | "edge" | "brave" | ...</summary>
    public string CookiesFromBrowser { get; set; } = string.Empty;
    /// <summary>Optional browser profile name.</summary>
    public string CookiesBrowserProfile { get; set; } = string.Empty;
    public string CookiesFile { get; set; } = string.Empty;
    public string Proxy { get; set; } = string.Empty;
    /// <summary>e.g. "5M". Empty = no limit.</summary>
    public string RateLimit { get; set; } = string.Empty;
    public int? ConcurrentFragments { get; set; }
    public int? Retries { get; set; }

    // --- Playlist ---
    public bool? DownloadPlaylist { get; set; }
    /// <summary>e.g. "1-5,8".</summary>
    public string PlaylistItems { get; set; } = string.Empty;
    public bool PlaylistReverse { get; set; }
    public bool PlaylistRandom { get; set; }

    // --- Misc ---
    public bool ForceOverwrites { get; set; }
    public bool ContinuePartial { get; set; } = true;
    public bool Verbose { get; set; }
    /// <summary>Free-form, appended last as-is (split on whitespace by ArgBuilder).</summary>
    public string ExtraArgs { get; set; } = string.Empty;

    public DownloadOptions Clone()
    {
        return (DownloadOptions)MemberwiseClone();
    }
}

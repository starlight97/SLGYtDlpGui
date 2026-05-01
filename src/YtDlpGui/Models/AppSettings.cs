namespace YtDlpGui.Models;

/// <summary>
/// Persisted to <c>%LOCALAPPDATA%\YtDlpGui\settings.json</c> via System.Text.Json.
/// SPEC §5.6: paths, default output folder, parallelism. Theming and the profile
/// system are v0.4; <see cref="LastOptions"/> serves as the v0.3 single-profile
/// stand-in so the Options panel restores between runs.
/// </summary>
public sealed class AppSettings
{
    /// <summary>If non-empty and the file exists, overrides <c>BinaryResolver</c> auto-detection.</summary>
    public string YtDlpPathOverride { get; set; } = string.Empty;

    /// <summary>If non-empty and the file exists, overrides <c>BinaryResolver</c> auto-detection.</summary>
    public string FfmpegPathOverride { get; set; } = string.Empty;

    /// <summary>Used as the OutputFolder seed only when <see cref="LastOptions"/> is null (first run).</summary>
    public string DefaultOutputFolder { get; set; } = string.Empty;

    public int Parallelism { get; set; } = 2;

    /// <summary>"System" | "Light" | "Dark". Drives WPF-UI's theme manager at startup.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// Last-committed Options panel state. Restored on startup so the user doesn't
    /// re-tick every option each session. Null on first run; OptionsViewModel falls
    /// back to its hard-coded defaults in that case.
    /// </summary>
    public DownloadOptions? LastOptions { get; set; }
}

namespace YtDlpGui.Services;

public sealed record YtDlpRunResult(int ExitCode, bool SawErrorPrefix);

public interface IYtDlpRunner
{
    /// <summary>
    /// Runs yt-dlp with the given args. Streams every stdout/stderr line to
    /// <paramref name="onLine"/> on a background thread. The caller is responsible
    /// for marshaling to the UI thread.
    /// </summary>
    /// <param name="ytDlpPath">Absolute path to yt-dlp.exe.</param>
    /// <param name="args">Argv to pass; will use ProcessStartInfo.ArgumentList.</param>
    /// <param name="workingDirectory">Working directory for the process (typically the output folder).</param>
    /// <param name="onLine">Callback invoked once per output line (stdout + stderr interleaved).</param>
    /// <param name="ct">Cancellation token; cancellation kills the process tree.</param>
    Task<YtDlpRunResult> RunAsync(
        string ytDlpPath,
        IReadOnlyList<string> args,
        string? workingDirectory,
        Action<string> onLine,
        CancellationToken ct);
}

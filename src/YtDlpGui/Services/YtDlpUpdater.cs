using System.IO;
using System.Text;
using Serilog;
using YtDlpGui.Infrastructure;

namespace YtDlpGui.Services;

/// <summary>Result of a version probe. All fields null means yt-dlp couldn't be found/run/parsed.</summary>
public sealed record YtDlpVersionCheck(string? YtDlpPath, string? Version, DateOnly? ReleaseDate);

/// <summary>
/// <see cref="ExitCode"/> is null when the process never ran (not found / already updating / launch failed).
/// <see cref="Output"/> is trimmed stdout+stderr; <see cref="Message"/> is a one-line summary for banners.
/// </summary>
public sealed record YtDlpSelfUpdateResult(bool Success, int? ExitCode, string Output, string Message);

/// <summary>
/// Wraps <c>yt-dlp --version</c> / <c>yt-dlp -U</c> so both the outdated-version banner
/// (SPEC F1) and (eventually) Settings' self-update button share one implementation and one
/// "only one -U at a time" guard — running two at once would race on replacing the same exe.
/// </summary>
public sealed class YtDlpUpdater
{
    private readonly IBinaryResolver _binaries;
    private readonly IYtDlpRunner _runner;
    private int _updating;

    public YtDlpUpdater(IBinaryResolver binaries, IYtDlpRunner runner)
    {
        _binaries = binaries;
        _runner = runner;
    }

    public bool IsUpdating => Volatile.Read(ref _updating) == 1;

    /// <summary>
    /// Raised right after <see cref="IsUpdating"/> flips (on start, and again in the finally),
    /// on whatever thread <see cref="SelfUpdateAsync"/> runs on. <see cref="IsUpdating"/> is a
    /// singleton flag shared by the singleton MainViewModel and the transient SettingsViewModel —
    /// this event is how each one learns the OTHER started/finished a self-update.
    /// </summary>
    public event Action? IsUpdatingChanged;

    /// <summary>Runs <c>yt-dlp --version</c> and parses the result. Never throws.</summary>
    public async Task<YtDlpVersionCheck> CheckVersionAsync(CancellationToken ct)
    {
        var path = _binaries.ResolveYtDlp();
        if (path is null || !File.Exists(path))
        {
            Log.Information("yt-dlp version check skipped: yt-dlp.exe not found");
            return new YtDlpVersionCheck(null, null, null);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // --version is safe to kill if it hangs

            var sb = new StringBuilder();
            var result = await _runner.RunAsync(
                path,
                new[] { "--version" },
                workingDirectory: null,
                onLine: line => sb.AppendLine(line),
                ct: cts.Token).ConfigureAwait(false);

            // YtDlpRunner kills the process on cancellation but then awaits its exit with
            // CancellationToken.None, so a 30s timeout completes RunAsync normally (with the
            // killed process's exit code) rather than throwing — check the token explicitly
            // so a real hang is logged as "timed out", not lumped in with "unparseable".
            if (cts.IsCancellationRequested)
            {
                Log.Warning("yt-dlp --version timed out ({Path})", path);
                return new YtDlpVersionCheck(path, null, null);
            }

            var output = sb.ToString();
            if (result.ExitCode != 0 || !YtDlpVersionParser.TryParse(output, out var version, out var date))
            {
                Log.Warning("yt-dlp --version output unparseable ({Path}): {Output}", path, output.Trim());
                return new YtDlpVersionCheck(path, null, null);
            }

            Log.Information("yt-dlp version {Version} ({Path})", version, path);
            return new YtDlpVersionCheck(path, version, date);
        }
        catch (OperationCanceledException)
        {
            // Kept defensively in case a future IYtDlpRunner implementation does observe the
            // token and throw on cancellation (see the IsCancellationRequested check above).
            Log.Warning("yt-dlp --version timed out ({Path})", path);
            return new YtDlpVersionCheck(path, null, null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "yt-dlp --version could not run ({Path})", path);
            return new YtDlpVersionCheck(path, null, null);
        }
    }

    /// <summary>Runs <c>yt-dlp -U --no-color</c>. Never throws; rejects concurrent calls.</summary>
    public async Task<YtDlpSelfUpdateResult> SelfUpdateAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0)
            return new YtDlpSelfUpdateResult(false, null, string.Empty, "Another yt-dlp update is already running.");

        try
        {
            // Raised inside the try (not before it) so a throwing subscriber still lets the
            // finally below reset _updating — otherwise the flag would be stuck at 1 forever.
            RaiseIsUpdatingChanged();
            var path = _binaries.ResolveYtDlp();
            if (path is null || !File.Exists(path))
                return new YtDlpSelfUpdateResult(false, null, string.Empty, "yt-dlp.exe not found.");

            Log.Information("yt-dlp -U starting ({Path})", path);
            try
            {
                var sb = new StringBuilder();
                // No timeout here: killing yt-dlp.exe mid-self-replace risks a corrupted binary.
                var result = await _runner.RunAsync(
                    path,
                    new[] { "-U", "--no-color" },
                    workingDirectory: null,
                    onLine: line => sb.AppendLine(line),
                    ct: ct).ConfigureAwait(false);

                var output = sb.ToString().Trim();
                var success = result.ExitCode == 0 && !result.SawErrorPrefix
                    && !output.Contains("Use that to update", StringComparison.OrdinalIgnoreCase);

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string message;
                if (success)
                {
                    message = lines.LastOrDefault()?.Trim() ?? "Done.";
                    Log.Information("yt-dlp -U succeeded (exit {Exit}): {Output}", result.ExitCode, output);
                }
                else
                {
                    message = lines.LastOrDefault(l => l.TrimStart().StartsWith("ERROR:", StringComparison.Ordinal))?.Trim()
                        ?? lines.LastOrDefault()?.Trim()
                        ?? $"exit {result.ExitCode}";
                    Log.Warning("yt-dlp -U failed (exit {Exit}): {Output}", result.ExitCode, output);
                }

                return new YtDlpSelfUpdateResult(success, result.ExitCode, output, message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "yt-dlp -U could not run ({Path})", path);
                return new YtDlpSelfUpdateResult(false, null, string.Empty, ex.Message);
            }
        }
        finally
        {
            Volatile.Write(ref _updating, 0);
            RaiseIsUpdatingChanged();
        }
    }

    /// <summary>Raises <see cref="IsUpdatingChanged"/>, tolerating a throwing subscriber so
    /// <see cref="SelfUpdateAsync"/> keeps its "Never throws" contract — a bad handler is logged
    /// and swallowed rather than escaping and skipping the flag reset / finish notification.</summary>
    private void RaiseIsUpdatingChanged()
    {
        try
        {
            IsUpdatingChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "IsUpdatingChanged subscriber threw");
        }
    }
}

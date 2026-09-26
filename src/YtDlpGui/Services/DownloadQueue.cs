using System.Collections.Concurrent;
using System.IO;
using Serilog;
using YtDlpGui.Infrastructure;
using YtDlpGui.Infrastructure.Hangul;
using YtDlpGui.Models;

namespace YtDlpGui.Services;

public sealed class DownloadQueue : IDownloadQueue, IDisposable
{
    private readonly IBinaryResolver _binaries;
    private readonly IYtDlpRunner _runner;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _ctsById = new();

    private SemaphoreSlim _gate = new(initialCount: 2, maxCount: 64);
    private int _parallelism = 2;

    public DownloadQueue(IBinaryResolver binaries, IYtDlpRunner runner)
    {
        _binaries = binaries;
        _runner = runner;
    }

    public bool IsInFlight(Guid id) => _ctsById.ContainsKey(id);

    public int Parallelism
    {
        get => _parallelism;
        set
        {
            if (value < 1) value = 1;
            if (value == _parallelism) return;
            // Replace the semaphore. Anything already waiting on the old one will be released
            // when the items finish; the new semaphore governs all subsequent acquisitions.
            // This is intentionally simple — v0.2 doesn't need live re-balancing across changes.
            _parallelism = value;
            var old = _gate;
            _gate = new SemaphoreSlim(value, 64);
            old.Dispose();
        }
    }

    public event Action<DownloadItem, DownloadStatus>? StatusChanged;
    public event Action<DownloadItem, ProgressSnapshot>? ProgressUpdated;
    public event Action<DownloadItem, string>? LogLine;
    public event Action<DownloadItem, DownloadFinalResult>? Finished;

    public void Enqueue(DownloadItem item)
    {
        Log.Information("Enqueue {Url}", item.Url);
        // Fire-and-forget — the queue's lifecycle owns the task; failures are reported via Finished.
        _ = Task.Run(() => RunAsync(item));
    }

    public void CancelItem(Guid id)
    {
        if (_ctsById.TryGetValue(id, out var cts))
        {
            try { cts.Cancel(); } catch { /* already disposed */ }
        }
    }

    public void CancelAll()
    {
        foreach (var cts in _ctsById.Values)
        {
            try { cts.Cancel(); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        CancelAll();
        _gate.Dispose();
        foreach (var cts in _ctsById.Values)
        {
            try { cts.Dispose(); } catch { }
        }
        _ctsById.Clear();
    }

    private async Task RunAsync(DownloadItem item)
    {
        var cts = new CancellationTokenSource();
        _ctsById[item.Id] = cts;
        StatusChanged?.Invoke(item, DownloadStatus.Queued);

        try
        {
            await _gate.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            EmitFinal(item, DownloadStatus.Canceled, exitCode: -1, reason: "Canceled before start");
            CleanupCts(item.Id);
            return;
        }

        try
        {
            await ExecuteAsync(item, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            try { _gate.Release(); } catch { /* gate may have been replaced */ }
            CleanupCts(item.Id);
        }
    }

    private async Task ExecuteAsync(DownloadItem item, CancellationToken ct)
    {
        var ytDlpPath = _binaries.ResolveYtDlp();
        if (ytDlpPath is null || !File.Exists(ytDlpPath))
        {
            LogLine?.Invoke(item, "[gui] yt-dlp.exe not found (PATH or app folder).");
            EmitFinal(item, DownloadStatus.Failed, exitCode: -1, reason: "yt-dlp not found");
            return;
        }

        try { Directory.CreateDirectory(item.Options.OutputFolder); }
        catch (Exception ex)
        {
            LogLine?.Invoke(item, $"[gui] cannot create output folder: {ex.Message}");
            EmitFinal(item, DownloadStatus.Failed, exitCode: -1, reason: "Output folder error");
            return;
        }

        // null = not found anywhere; omit the flag and let yt-dlp fall back to its own lookup.
        var ffmpegPath = _binaries.ResolveFfmpeg();
        var args = ArgBuilder.Build(item.Options, item.Url, ffmpegPath);

        // §13: log argv on line 1 of the per-item log.
        LogLine?.Invoke(item, ArgBuilder.FormatForLog(ytDlpPath, args));

        item.StartedAtUtc = DateTime.UtcNow;
        StatusChanged?.Invoke(item, DownloadStatus.Resolving);

        var lastStatus = DownloadStatus.Resolving;

        void OnLine(string line)
        {
            // PROGRESS: → structured snapshot, not echoed to log (would spam).
            if (ProgressParser.TryParseProgress(line, out var snap))
            {
                ProgressUpdated?.Invoke(item, snap);
                return;
            }

            // FILEPATH: → capture the resolved final-on-disk path for "Open file".
            // Single line at end of run; don't echo to per-item log (it's redundant
            // with the [download] Destination lines that ARE echoed).
            if (line.StartsWith(ArgBuilder.FilePathPrefix, StringComparison.Ordinal))
            {
                var path = line[ArgBuilder.FilePathPrefix.Length..].Trim();
                if (path.Length > 0) item.FinalFilePath = path;
                return;
            }

            // Stage tag → status transition (forward-only: don't slip back to Resolving once Downloading).
            var inferred = ProgressParser.TryInferStatus(line);
            if (inferred is DownloadStatus s && (int)s > (int)lastStatus)
            {
                lastStatus = s;
                StatusChanged?.Invoke(item, s);
            }

            LogLine?.Invoke(item, line);
        }

        YtDlpRunResult result;
        try
        {
            result = await _runner.RunAsync(
                ytDlpPath,
                args,
                workingDirectory: item.Options.OutputFolder,
                onLine: OnLine,
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogLine?.Invoke(item, $"[gui] runner crashed: {ex.GetType().Name}: {ex.Message}");
            item.FinishedAtUtc = DateTime.UtcNow;
            EmitFinal(item, DownloadStatus.Failed, exitCode: -1, reason: ex.Message);
            return;
        }

        item.FinishedAtUtc = DateTime.UtcNow;

        DownloadStatus final;
        string? reason = null;
        if (ct.IsCancellationRequested)
        {
            final = DownloadStatus.Canceled;
            reason = "Canceled by user";
        }
        else if (result.ExitCode == 0 && !result.SawErrorPrefix)
        {
            final = DownloadStatus.Done;

            // §8: NFC sweep. Restricted to files modified during this run so concurrent
            // downloads don't fight over each other's filenames.
            if (item.Options.NfcNormalize && item.StartedAtUtc is DateTime started)
            {
                try
                {
                    var since = started.AddSeconds(-5); // slack for clock/fs granularity
                    var renamed = NfcNormalizer.NormalizeFolder(
                        item.Options.OutputFolder,
                        modifiedSince: since,
                        log: m => LogLine?.Invoke(item, m));
                    if (renamed == 0) LogLine?.Invoke(item, "[gui] NFC: nothing to normalize.");
                }
                catch (Exception ex)
                {
                    LogLine?.Invoke(item, $"[gui] NFC sweep failed: {ex.Message}");
                }
            }
        }
        else
        {
            final = DownloadStatus.Failed;
            reason = result.SawErrorPrefix ? "ERROR: line emitted" : $"exit code {result.ExitCode}";
        }

        EmitFinal(item, final, result.ExitCode, reason);
    }

    private void EmitFinal(DownloadItem item, DownloadStatus final, int exitCode, string? reason)
    {
        var elapsed = (item.FinishedAtUtc ?? DateTime.UtcNow) - (item.StartedAtUtc ?? item.EnqueuedAtUtc);
        if (final == DownloadStatus.Done)
            Log.Information("Done {Url} in {Elapsed}", item.Url, elapsed);
        else
            Log.Warning("Finished {Url} status={Status} exit={Exit} reason={Reason}",
                item.Url, final, exitCode, reason ?? "(none)");

        StatusChanged?.Invoke(item, final);
        Finished?.Invoke(item, new DownloadFinalResult(final, exitCode, reason));
    }

    private void CleanupCts(Guid id)
    {
        if (_ctsById.TryRemove(id, out var cts))
        {
            try { cts.Dispose(); } catch { }
        }
    }
}

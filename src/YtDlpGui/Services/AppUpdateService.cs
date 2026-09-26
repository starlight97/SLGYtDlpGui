using System.Diagnostics;
using Serilog;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace YtDlpGui.Services;

public sealed class AppUpdateService : IAppUpdateService
{
    /// <summary>Keep in sync with $RepoUrl in build/pack.ps1.</summary>
    private const string GithubRepoUrl = "https://github.com/starlight97/SLGYtDlpGui";

    /// <summary>Test hook: point at a local vpk output folder (...\Releases) to exercise the flow without publishing.</summary>
    private const string FeedOverrideVariable = "YTDLPGUI_UPDATE_FEED";

    private static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromSeconds(60);

    // Process-lifetime hand-off to Program.Main, consumed after App.Run() returns.
    private static volatile ScheduledApply? _scheduled;
    private sealed record ScheduledApply(UpdateManager Manager, VelopackAsset Asset);

    private readonly UpdateManager? _manager;
    private readonly TimeSpan _checkTimeout;
    private UpdateInfo? _pending;          // result of the last check that found an update
    private VelopackAsset? _readyAsset;    // downloaded (this or an earlier session), not applied yet
    private int _busy;                     // 1 while any Velopack call runs — including an abandoned check
    private volatile AppUpdateStatus _status;

    public AppUpdateService() : this(source: null, locator: null) { }

    /// <summary>
    /// Test seam: VelopackApp.Run() never executes in the test host, so pass a locator explicitly
    /// (VelopackLocator.CreateDefaultForPlatform() = not installed; TestVelopackLocator = installed).
    /// </summary>
    internal AppUpdateService(IUpdateSource? source, IVelopackLocator? locator, TimeSpan? checkTimeout = null)
    {
        _checkTimeout = checkTimeout ?? DefaultCheckTimeout;
        try
        {
            _manager = CreateManager(source, locator);
            IsInstalled = _manager.IsInstalled;
            // Left over when startup auto-apply was skipped (another instance) or failed.
            if (IsInstalled) _readyAsset = _manager.UpdatePendingRestart;
        }
        catch (Exception ex)
        {
            // e.g. "No VelopackLocator has been set" when Run() didn't execute.
            Log.Warning(ex, "Velopack UpdateManager init failed; app updates disabled");
            _manager = null;
            IsInstalled = false;
        }

        var current = _manager?.CurrentVersion?.ToString()
            ?? typeof(AppUpdateService).Assembly.GetName().Version?.ToString(3) ?? "?";
        _status = !IsInstalled
            ? new AppUpdateStatus(AppUpdateState.NotInstalled, current)
            : _readyAsset is { } ready
                ? new AppUpdateStatus(AppUpdateState.ReadyToRestart, current, ready.Version.ToString(), 100)
                : new AppUpdateStatus(AppUpdateState.Idle, current);
        Log.Information("App update: installed={Installed} portable={Portable} version={Version} pending={Pending} cwd={Cwd}",
            IsInstalled, _manager?.IsPortable ?? false, current, _readyAsset?.Version, Environment.CurrentDirectory);
    }

    private static UpdateManager CreateManager(IUpdateSource? source, IVelopackLocator? locator)
    {
        if (source is not null) return new UpdateManager(source, null, locator);
        var feed = Environment.GetEnvironmentVariable(FeedOverrideVariable);
        if (!string.IsNullOrWhiteSpace(feed))
        {
            Log.Warning("App update feed overridden by {Variable}: {Feed}", FeedOverrideVariable, feed);
            return new UpdateManager(feed, null, locator);
        }
        return new UpdateManager(new GithubSource(GithubRepoUrl, accessToken: null, prerelease: false), null, locator);
    }

    public bool IsInstalled { get; }
    public AppUpdateStatus Status => _status;
    public bool IsApplyScheduled => _scheduled is not null;
    public event Action<AppUpdateStatus>? StatusChanged;

    /// <summary>Test probe: true while a Velopack call (including an abandoned, timed-out check) is running.</summary>
    internal bool IsBusy => Volatile.Read(ref _busy) != 0;

    public bool IsAnotherInstanceRunning() => IsAnotherInstanceRunningCore();

    public async Task<AppUpdateStatus> CheckAsync(CancellationToken ct = default)
    {
        var mgr = _manager;
        if (mgr is null || !IsInstalled) return _status;
        if (_status.State == AppUpdateState.ReadyToRestart) return _status;   // already downloaded
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return _status;

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(_checkTimeout);
        Task<UpdateInfo?>? check = null;
        var releaseBusy = true;
        try
        {
            Publish(_status with { State = AppUpdateState.Checking, Error = null });
            check = Task.Run(() => mgr.CheckForUpdatesAsync());
            // CheckForUpdatesAsync takes no token: WaitAsync only stops *waiting*; the call keeps running.
            var info = await check.WaitAsync(waitCts.Token).ConfigureAwait(false);
            _pending = info;
            if (info is null)
            {
                Log.Information("App is up to date ({Version})", _status.CurrentVersion);
                return Publish(_status with { State = AppUpdateState.UpToDate, AvailableVersion = null });
            }
            var available = info.TargetFullRelease.Version.ToString();
            Log.Information("App update available: {Current} -> {Available}", _status.CurrentVersion, available);
            return Publish(_status with { State = AppUpdateState.Available, AvailableVersion = available, Progress = 0 });
        }
        catch (OperationCanceledException) when (waitCts.IsCancellationRequested)
        {
            if (check is { IsCompleted: false })
            {
                // Keep _busy until Velopack really returns so a retry can't start a second, overlapping
                // call; the late result is discarded.
                releaseBusy = false;
                _ = check.ContinueWith(ReleaseAfterAbandonedCheck, CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
            }
            if (ct.IsCancellationRequested) return Publish(_status with { State = AppUpdateState.Idle });
            Log.Warning("App update check timed out after {Timeout}", _checkTimeout);
            return Publish(_status with { State = AppUpdateState.Failed, Error = "Timed out waiting for GitHub. Try again in a minute." });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "App update check failed");
            return Publish(_status with { State = AppUpdateState.Failed, Error = ex.Message });
        }
        finally
        {
            if (releaseBusy) Volatile.Write(ref _busy, 0);
        }
    }

    private void ReleaseAfterAbandonedCheck(Task<UpdateInfo?> t)
    {
        if (t.IsFaulted) Log.Warning(t.Exception, "Abandoned app update check failed late"); // observes the exception
        else Log.Information("Abandoned app update check finished late; result discarded");
        Volatile.Write(ref _busy, 0);
    }

    public async Task<AppUpdateStatus> DownloadAsync(CancellationToken ct = default)
    {
        var mgr = _manager;
        var info = _pending;
        if (mgr is null || info is null || _status.State != AppUpdateState.Available) return _status;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return _status;

        try
        {
            Publish(_status with { State = AppUpdateState.Downloading, Progress = 0, Error = null });
            var last = -1;
            // DownloadUpdatesAsync honours ct, and awaiting Task.Run awaits the real task → finally is safe here.
            await Task.Run(() => mgr.DownloadUpdatesAsync(info, p =>
            {
                if (p == last) return;   // worker thread, 0..100
                last = p;
                Publish(_status with { Progress = p });
            }, ct), ct).ConfigureAwait(false);
            _readyAsset = info.TargetFullRelease;
            Log.Information("App update {Version} downloaded", _status.AvailableVersion);
            return Publish(_status with { State = AppUpdateState.ReadyToRestart, Progress = 100 });
        }
        catch (OperationCanceledException)
        {
            Log.Information("App update download canceled");
            return Publish(_status with { State = AppUpdateState.Available, Progress = 0 });
        }
        catch (Exception ex)
        {
            // Includes AcquireLockFailedException (another Velopack operation, e.g. a second instance, holds the lock).
            Log.Warning(ex, "App update download failed");
            return Publish(_status with { State = AppUpdateState.Failed, Error = ex.Message });
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    public bool ScheduleApplyOnExit()
    {
        var mgr = _manager;
        var asset = _readyAsset;
        if (mgr is null || asset is null || _status.State != AppUpdateState.ReadyToRestart) return false;
        _scheduled = new ScheduledApply(mgr, asset);
        Log.Information("App update {Version} will be applied after exit", asset.Version);
        // IsApplyScheduled isn't part of the Status record, but it's read by MainViewModel/SettingsViewModel
        // CanExecute predicates via StatusChanged subscribers — re-publish so both VMs requery, even the
        // one that didn't call this method (e.g. Settings schedules, MainWindow.OnClosing cancels).
        Publish(_status);
        return true;
    }

    public void CancelScheduledApply()
    {
        if (_scheduled is null) return;
        _scheduled = null;
        Log.Information("Scheduled app update canceled (app kept running)");
        Publish(_status);
    }

    /// <summary>
    /// Called by Program.Main after App.Run() returns (settings saved, Serilog closed → Debug only).
    /// Update.exe waits up to 60 s for this process, then force-stops anything left under the install
    /// root, applies and restarts. If launching it fails, the package is applied on the next launch.
    /// </summary>
    internal static void ApplyScheduledUpdateOnExit()
    {
        var scheduled = _scheduled;
        if (scheduled is null) return;
        try
        {
            scheduled.Manager.WaitExitThenApplyUpdates(scheduled.Asset, silent: false, restart: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WaitExitThenApplyUpdates failed: {ex}");
        }
    }

    /// <summary>
    /// Another process running this exact exe? Velopack's apply step force-kills every process under the
    /// install root without a Closing prompt. Processes we can't inspect count as "yes" (conservative).
    /// </summary>
    internal static bool IsAnotherInstanceRunningCore()
    {
        var found = false;
        try
        {
            using var self = Process.GetCurrentProcess();
            var selfPath = Environment.ProcessPath;
            foreach (var p in Process.GetProcessesByName(self.ProcessName))
            {
                using (p)
                {
                    if (found || p.Id == self.Id) continue;
                    try { found = string.Equals(p.MainModule?.FileName, selfPath, StringComparison.OrdinalIgnoreCase); }
                    catch { found = true; }
                }
            }
        }
        catch
        {
            // Enumeration failed: assume we're alone.
        }
        return found;
    }

    private AppUpdateStatus Publish(AppUpdateStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(status);
        return status;
    }
}

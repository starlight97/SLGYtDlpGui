using System.IO;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;
using Xunit;
using YtDlpGui.Services;

namespace YtDlpGui.Tests;

/// <summary>
/// Exercises <see cref="AppUpdateService"/> against real Velopack locators/sources (no network:
/// <see cref="SimpleFileSource"/> reads a local folder), using the internal (source, locator,
/// checkTimeout) test seam ctor since <c>VelopackApp.Run()</c> never executes in the test host.
/// </summary>
public class AppUpdateServiceTests
{
    [Fact]
    public void Default_ctor_without_VelopackApp_Run_disables_updates()
    {
        // No VelopackLocator.Current has been set (Run() never executed here) -> the real ctor
        // path throws "No VelopackLocator has been set"; AppUpdateService swallows it (see ctor).
        var service = new AppUpdateService();

        Assert.False(service.IsInstalled);
        Assert.Equal(AppUpdateState.NotInstalled, service.Status.State);
        Assert.False(service.ScheduleApplyOnExit());
    }

    [Fact]
    public async Task Default_ctor_CheckAsync_returns_NotInstalled_without_throwing()
    {
        var service = new AppUpdateService();

        var status = await service.CheckAsync();

        Assert.Equal(AppUpdateState.NotInstalled, status.State);
    }

    [Fact]
    public async Task Real_not_installed_locator_reports_NotInstalled()
    {
        using var tmp = new TempDir();
        var service = new AppUpdateService(
            new SimpleFileSource(new DirectoryInfo(tmp.Path)),
            VelopackLocator.CreateDefaultForPlatform());

        Assert.False(service.IsInstalled);
        Assert.Equal(AppUpdateState.NotInstalled, service.Status.State);

        // Must not throw NotInstalledException - IsInstalled guards CheckAsync before it calls Velopack.
        var status = await service.CheckAsync();
        Assert.Equal(AppUpdateState.NotInstalled, status.State);
    }

    [Fact]
    public async Task Installed_empty_feed_is_up_to_date()
    {
        using var tmp = new TempDir(); // no releases.win.json -> empty feed
        var service = new AppUpdateService(
            new SimpleFileSource(new DirectoryInfo(tmp.Path)),
            new TestVelopackLocator("SLGYtDlpGui", "0.4.0", tmp.Path));

        Assert.True(service.IsInstalled);
        Assert.Equal(AppUpdateState.Idle, service.Status.State);

        var seen = new List<AppUpdateState>();
        service.StatusChanged += s => { lock (seen) seen.Add(s.State); };

        var status = await service.CheckAsync();

        Assert.Equal(AppUpdateState.UpToDate, status.State);
        Assert.Equal(new[] { AppUpdateState.Checking, AppUpdateState.UpToDate }, seen);
    }

    [Fact]
    public async Task Installed_feed_with_newer_release_is_available()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "releases.win.json"),
            """
            {"Assets":[{"PackageId":"SLGYtDlpGui","Version":"0.5.0","Type":"Full","FileName":"SLGYtDlpGui-0.5.0-full.nupkg","SHA1":"0000000000000000000000000000000000000000","SHA256":"","Size":10}]}
            """);
        var service = new AppUpdateService(
            new SimpleFileSource(new DirectoryInfo(tmp.Path)),
            new TestVelopackLocator("SLGYtDlpGui", "0.4.0", tmp.Path));

        var status = await service.CheckAsync();

        Assert.Equal(AppUpdateState.Available, status.State);
        Assert.Equal("0.5.0", status.AvailableVersion);
        Assert.False(service.ScheduleApplyOnExit()); // not downloaded yet
    }

    [Fact]
    public async Task Timed_out_check_keeps_busy_until_velopack_returns()
    {
        var source = new BlockingUpdateSource();
        var service = new AppUpdateService(
            source,
            new TestVelopackLocator("SLGYtDlpGui", "0.4.0", Path.GetTempPath()),
            checkTimeout: TimeSpan.FromMilliseconds(100));

        var first = await service.CheckAsync();
        Assert.Equal(AppUpdateState.Failed, first.State);
        Assert.True(service.IsBusy);
        Assert.Equal(1, source.CallCount);

        // Retrying while the abandoned Velopack call is still in flight must not start a second,
        // overlapping call (D13) - it's a no-op that returns the current (Failed) status.
        var retry = await service.CheckAsync();
        Assert.Equal(AppUpdateState.Failed, retry.State);
        Assert.Equal(1, source.CallCount);

        source.CompleteWithEmptyFeed();
        await WaitUntilAsync(() => !service.IsBusy, TimeSpan.FromSeconds(5));

        var third = await service.CheckAsync();
        Assert.Equal(AppUpdateState.UpToDate, third.State);
        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public void ScheduleApplyOnExit_then_CancelScheduledApply_raise_StatusChanged()
    {
        // TestVelopackLocator's (…, localPackage, …) overload seeds UpdateManager.UpdatePendingRestart
        // directly, reaching ReadyToRestart without a real download/apply round-trip (fabricating a
        // checksum-valid nupkg for that is out of scope for a fast unit test).
        using var tmp = new TempDir();
        var readyAsset = new VelopackAsset
        {
            PackageId = "SLGYtDlpGui",
            Version = SemanticVersion.Parse("0.5.0"),
            Type = VelopackAssetType.Full,
            FileName = "SLGYtDlpGui-0.5.0-full.nupkg",
            SHA1 = "0000000000000000000000000000000000000000",
            SHA256 = "",
            Size = 10,
        };
        var locator = new TestVelopackLocator(
            "SLGYtDlpGui", "0.4.0", "0.5.0", tmp.Path, tmp.Path, "Update.exe", "win",
            logger: null!, localPackage: readyAsset, processPath: null!);
        var service = new AppUpdateService(new SimpleFileSource(new DirectoryInfo(tmp.Path)), locator);
        Assert.Equal(AppUpdateState.ReadyToRestart, service.Status.State);

        var raisedCount = 0;
        service.StatusChanged += _ => raisedCount++;
        try
        {
            Assert.True(service.ScheduleApplyOnExit());
            Assert.True(service.IsApplyScheduled);
            Assert.Equal(1, raisedCount);

            service.CancelScheduledApply();
            Assert.False(service.IsApplyScheduled);
            Assert.Equal(2, raisedCount);
        }
        finally
        {
            // _scheduled is a process-static field (hand-off to Program.Main) — make sure a failed
            // assertion above can't leak a "scheduled" state into every other test reading IsApplyScheduled.
            service.CancelScheduledApply();
        }
    }

    [Fact]
    public void CancelScheduledApply_with_nothing_scheduled_does_not_raise_StatusChanged()
    {
        var service = new AppUpdateService(); // not installed; never scheduled anything
        Assert.False(service.IsApplyScheduled);

        var raised = false;
        service.StatusChanged += _ => raised = true;

        service.CancelScheduledApply(); // no-op

        Assert.False(raised);
    }

    [Fact]
    public async Task Caller_cancel_returns_idle()
    {
        var source = new BlockingUpdateSource();
        var service = new AppUpdateService(
            source,
            new TestVelopackLocator("SLGYtDlpGui", "0.4.0", Path.GetTempPath()),
            checkTimeout: TimeSpan.FromSeconds(30));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var status = await service.CheckAsync(cts.Token);

        Assert.Equal(AppUpdateState.Idle, status.State);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    /// <summary>Deterministic replacement for CheckForUpdatesAsync's missing CancellationToken (D13):
    /// the returned task only completes when the test calls <see cref="CompleteWithEmptyFeed"/>.</summary>
    private sealed class BlockingUpdateSource : IUpdateSource
    {
        private readonly TaskCompletionSource<VelopackAssetFeed> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _callCount;
        public int CallCount => _callCount;

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
            Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            Interlocked.Increment(ref _callCount);
            return _tcs.Task;
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
            Action<int> progress, CancellationToken cancelToken = default)
            => throw new NotSupportedException();

        public void CompleteWithEmptyFeed() => _tcs.TrySetResult(new VelopackAssetFeed { Assets = Array.Empty<VelopackAsset>() });
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ytdlpgui-appupdate-tests-" + Guid.NewGuid());

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}

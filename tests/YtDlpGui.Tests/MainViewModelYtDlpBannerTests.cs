using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Xunit;
using YtDlpGui.Models;
using YtDlpGui.Services;
using YtDlpGui.ViewModels;

namespace YtDlpGui.Tests;

/// <summary>
/// Exercises the F1 outdated-yt-dlp banner + Update button wiring on <see cref="MainViewModel"/>
/// with fake <see cref="IBinaryResolver"/>/<see cref="IYtDlpRunner"/>/<see cref="IDownloadQueue"/> —
/// never launches a real process and never touches WPF's Application/Dispatcher (Application.Current
/// is null in this test host, so the queue-event marshaling in MainViewModel is a no-op; tests drive
/// state directly through the public VM surface instead, e.g. DownloadItemViewModel.ApplyStatus).
/// </summary>
public class MainViewModelYtDlpBannerTests : IDisposable
{
    // A real file is needed so File.Exists(path) checks in YtDlpUpdater/DownloadQueue pass.
    private readonly string _fakeExePath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_fakeExePath); } catch { /* best effort */ }
    }

    private (MainViewModel vm, SequencedRunner runner) CreateVm()
    {
        var runner = new SequencedRunner();
        var resolver = new FakeResolver(_fakeExePath);
        var updater = new YtDlpUpdater(resolver, runner);
        var settings = new FakeSettingsStore();
        var options = new OptionsViewModel(settings, new FakeProfileStore());
        var vm = new MainViewModel(
            resolver,
            new FakeDownloadQueue(),
            runner,
            options,
            settings,
            new NullServiceProvider(),
            updater);
        return (vm, runner);
    }

    // A release date old enough to be "outdated" no matter when this test runs.
    private const string VeryOldVersion = "2000.01.01";

    private static string TodayVersion() => DateTime.Now.ToString("yyyy.MM.dd");

    [Fact]
    public async Task CheckYtDlpVersion_Outdated_ShowsBanner()
    {
        var (vm, runner) = CreateVm();
        runner.VersionLines.Enqueue(VeryOldVersion);

        await vm.CheckYtDlpVersionAsync();

        Assert.True(vm.HasOutdatedYtDlp);
        Assert.Contains(VeryOldVersion, vm.YtDlpBannerText);
    }

    [Fact]
    public async Task CheckYtDlpVersion_Fresh_NoBanner()
    {
        var (vm, runner) = CreateVm();
        runner.VersionLines.Enqueue(TodayVersion());

        await vm.CheckYtDlpVersionAsync();

        Assert.False(vm.HasOutdatedYtDlp);
    }

    [Fact]
    public async Task CheckYtDlpVersion_Unparseable_NoBanner()
    {
        var (vm, runner) = CreateVm();
        runner.VersionLines.Enqueue("not a version");

        await vm.CheckYtDlpVersionAsync();

        Assert.False(vm.HasOutdatedYtDlp);
    }

    [Fact]
    public async Task DismissYtDlpBanner_HidesBanner_AndStaysHiddenOnRecheckOfSameVersion()
    {
        var (vm, runner) = CreateVm();
        runner.VersionLines.Enqueue(VeryOldVersion);
        await vm.CheckYtDlpVersionAsync();
        Assert.True(vm.HasOutdatedYtDlp);

        vm.DismissYtDlpBannerCommand.Execute(null);
        Assert.False(vm.HasOutdatedYtDlp);

        // Re-running the check with the SAME outdated version must not resurrect the banner —
        // dismissal is remembered per-version for the rest of the session.
        runner.VersionLines.Enqueue(VeryOldVersion);
        await vm.CheckYtDlpVersionAsync();
        Assert.False(vm.HasOutdatedYtDlp);
    }

    [Fact]
    public async Task DismissYtDlpBanner_DifferentOutdatedVersion_ShowsAgain()
    {
        var (vm, runner) = CreateVm();
        runner.VersionLines.Enqueue(VeryOldVersion);
        await vm.CheckYtDlpVersionAsync();
        vm.DismissYtDlpBannerCommand.Execute(null);
        Assert.False(vm.HasOutdatedYtDlp);

        // A newer (but still outdated) version wasn't dismissed — banner should reappear.
        const string otherOldVersion = "2001.02.03";
        runner.VersionLines.Enqueue(otherOldVersion);
        await vm.CheckYtDlpVersionAsync();
        Assert.True(vm.HasOutdatedYtDlp);
        Assert.Contains(otherOldVersion, vm.YtDlpBannerText);
    }

    [Fact]
    public async Task UpdateYtDlp_Success_HidesBannerAndReportsNewVersion()
    {
        var (vm, runner) = CreateVm();
        runner.VersionLines.Enqueue(VeryOldVersion);
        await vm.CheckYtDlpVersionAsync();
        Assert.True(vm.HasOutdatedYtDlp);

        var freshVersion = TodayVersion();
        runner.SelfUpdateLines = new[] { $"Updated yt-dlp to {freshVersion}" };
        runner.SelfUpdateResult = new YtDlpRunResult(0, false);
        runner.VersionLines.Enqueue(freshVersion);

        await vm.UpdateYtDlpCommand.ExecuteAsync(null);

        Assert.False(vm.HasOutdatedYtDlp);
        Assert.Contains(freshVersion, vm.StatusText);
        Assert.False(vm.IsUpdatingYtDlp);
    }

    [Fact]
    public async Task UpdateYtDlp_Failure_KeepsBannerAndShowsErrorDetail()
    {
        var (vm, runner) = CreateVm();
        runner.VersionLines.Enqueue(VeryOldVersion);
        await vm.CheckYtDlpVersionAsync();
        Assert.True(vm.HasOutdatedYtDlp);

        runner.SelfUpdateLines = new[] { "ERROR: network unreachable" };
        runner.SelfUpdateResult = new YtDlpRunResult(1, true);

        await vm.UpdateYtDlpCommand.ExecuteAsync(null);

        Assert.True(vm.HasOutdatedYtDlp);
        Assert.Contains("network unreachable", vm.YtDlpBannerDetail);
        Assert.False(vm.IsUpdatingYtDlp);
    }

    [Fact]
    public async Task UpdateYtDlpCommand_CanExecute_FalseWhileUpdateInFlight()
    {
        var (vm, runner) = CreateVm();
        runner.SelfUpdateGate = new TaskCompletionSource<bool>();
        runner.SelfUpdateResult = new YtDlpRunResult(0, false);

        Assert.True(vm.UpdateYtDlpCommand.CanExecute(null));

        var task = vm.UpdateYtDlpCommand.ExecuteAsync(null);

        Assert.True(vm.IsUpdatingYtDlp);
        Assert.False(vm.UpdateYtDlpCommand.CanExecute(null));

        runner.SelfUpdateGate.SetResult(true);
        await task;

        Assert.False(vm.IsUpdatingYtDlp);
        Assert.True(vm.UpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public void UpdateYtDlpCommand_CanExecute_FalseWhileDownloadsActive()
    {
        var (vm, _) = CreateVm();
        vm.UrlsInput = "https://example.com/v";
        vm.AddToQueueCommand.Execute(null);

        var item = Assert.Single(vm.Items);
        Assert.False(item.IsTerminal);
        Assert.False(vm.UpdateYtDlpCommand.CanExecute(null));

        // Drive the item to a terminal state directly (Application.Current is null in this
        // test host, so the queue's StatusChanged event can't marshal through the VM's
        // Dispatcher.BeginInvoke path) — DownloadItemViewModel.ApplyStatus is the public
        // surface the production UI thread would otherwise reach via that dispatch.
        item.ApplyStatus(DownloadStatus.Done);

        Assert.True(vm.UpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public async Task UpdateYtDlp_CalledWhileDownloadsActive_KeepsBannerDetailAndDoesNotRun()
    {
        var (vm, runner) = CreateVm();
        vm.UrlsInput = "https://example.com/v";
        vm.AddToQueueCommand.Execute(null);
        Assert.False(vm.UpdateYtDlpCommand.CanExecute(null));

        // Even if something calls the underlying method directly (bypassing CanExecute),
        // it must refuse to run yt-dlp -U while a download is in flight.
        await vm.UpdateYtDlpCommand.ExecuteAsync(null);

        Assert.False(runner.SelfUpdateWasCalled);
        Assert.Contains("active downloads", vm.YtDlpBannerDetail, StringComparison.OrdinalIgnoreCase);
    }

    // --- fakes ---

    private sealed class FakeResolver : IBinaryResolver
    {
        private readonly string _path;
        public FakeResolver(string path) => _path = path;
        public string? ResolveYtDlp() => _path;
        public string? ResolveFfmpeg() => null;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public string SettingsFilePath => string.Empty;
        public void Save() { }
    }

    private sealed class FakeProfileStore : IProfileStore
    {
        public IReadOnlyList<Profile> All { get; } = Array.Empty<Profile>();
        public bool ExistsUserProfile(string name) => false;
        public void Upsert(Profile profile) { }
        public void Delete(string name) { }
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeDownloadQueue : IDownloadQueue
    {
        public int Parallelism { get; set; } = 2;
        public void Enqueue(DownloadItem item) { }
        public bool IsInFlight(Guid id) => false;
        public void CancelItem(Guid id) { }
        public void CancelAll() { }
#pragma warning disable CS0067 // MainViewModel subscribes, but these tests never need to raise them.
        public event Action<DownloadItem, DownloadStatus>? StatusChanged;
        public event Action<DownloadItem, ProgressSnapshot>? ProgressUpdated;
        public event Action<DownloadItem, string>? LogLine;
        public event Action<DownloadItem, DownloadFinalResult>? Finished;
#pragma warning restore CS0067
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>
    /// Fake <see cref="IYtDlpRunner"/> that discriminates by argv so a single instance can
    /// serve both the "--version" probe and the "-U" self-update within one test, mirroring
    /// how <see cref="YtDlpUpdater"/> actually drives both.
    /// </summary>
    private sealed class SequencedRunner : IYtDlpRunner
    {
        public Queue<string> VersionLines { get; } = new();
        public YtDlpRunResult VersionResult { get; set; } = new(0, false);

        public string[] SelfUpdateLines { get; set; } = Array.Empty<string>();
        public YtDlpRunResult SelfUpdateResult { get; set; } = new(0, false);
        public TaskCompletionSource<bool>? SelfUpdateGate { get; set; }
        public bool SelfUpdateWasCalled { get; private set; }

        public async Task<YtDlpRunResult> RunAsync(
            string ytDlpPath,
            IReadOnlyList<string> args,
            string? workingDirectory,
            Action<string> onLine,
            CancellationToken ct)
        {
            if (args.Contains("--version"))
            {
                if (VersionLines.Count > 0) onLine(VersionLines.Dequeue());
                return VersionResult;
            }

            if (args.Contains("-U"))
            {
                SelfUpdateWasCalled = true;
                if (SelfUpdateGate is not null) await SelfUpdateGate.Task.ConfigureAwait(false);
                foreach (var line in SelfUpdateLines) onLine(line);
                return SelfUpdateResult;
            }

            return new YtDlpRunResult(0, false);
        }
    }
}

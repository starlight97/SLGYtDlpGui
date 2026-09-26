using System.IO;
using System.Linq;
using Xunit;
using YtDlpGui.Models;
using YtDlpGui.Services;
using YtDlpGui.ViewModels;

namespace YtDlpGui.Tests;

/// <summary>
/// Reverse guard + cross-VM requery follow-up: the yt-dlp banner's Update button on
/// <see cref="MainViewModel"/> must never run alongside an app-update download or a scheduled
/// restart-to-apply (Velopack writes into / force-kills everything under the install folder during
/// those), and must also respect <see cref="YtDlpUpdater.IsUpdating"/> when a -U was started from the
/// OTHER live view model (SettingsViewModel) sharing the same singleton <see cref="YtDlpUpdater"/>.
/// </summary>
public class MainViewModelAppUpdateGuardTests : IDisposable
{
    // A real file is needed so File.Exists(path) checks in YtDlpUpdater/DownloadQueue pass.
    private readonly string _fakeExePath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_fakeExePath); } catch { /* best effort */ }
    }

    private (MainViewModel vm, FakeAppUpdateService appUpdates, YtDlpUpdater updater, GateRunner runner) CreateVm()
    {
        var runner = new GateRunner();
        var resolver = new FakeResolver(_fakeExePath);
        var updater = new YtDlpUpdater(resolver, runner);
        var settings = new FakeSettingsStore();
        var options = new OptionsViewModel(settings, new FakeProfileStore());
        var appUpdates = new FakeAppUpdateService { IsInstalled = true, Status = new AppUpdateStatus(AppUpdateState.Idle, "0.4.0") };
        var vm = new MainViewModel(
            resolver,
            new FakeDownloadQueue(),
            runner,
            options,
            settings,
            new NullServiceProvider(),
            updater,
            appUpdates);
        return (vm, appUpdates, updater, runner);
    }

    [Fact]
    public void UpdateYtDlpCommand_CanExecute_FalseWhileAppUpdateDownloading()
    {
        var (vm, appUpdates, _, _) = CreateVm();
        Assert.True(vm.UpdateYtDlpCommand.CanExecute(null));

        appUpdates.Status = new AppUpdateStatus(AppUpdateState.Downloading, "0.4.0", "0.5.0", 42);

        Assert.False(vm.UpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public void UpdateYtDlpCommand_CanExecute_TrueAgainAfterAppUpdateReturnsToIdle()
    {
        var (vm, appUpdates, _, _) = CreateVm();
        appUpdates.Status = new AppUpdateStatus(AppUpdateState.Downloading, "0.4.0", "0.5.0", 42);
        Assert.False(vm.UpdateYtDlpCommand.CanExecute(null));

        appUpdates.Status = new AppUpdateStatus(AppUpdateState.Idle, "0.4.0");

        Assert.True(vm.UpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public void UpdateYtDlpCommand_CanExecute_FalseWhileApplyScheduled()
    {
        var (vm, appUpdates, _, _) = CreateVm();
        appUpdates.IsApplyScheduled = true;

        Assert.False(vm.UpdateYtDlpCommand.CanExecute(null));

        appUpdates.IsApplyScheduled = false;
        Assert.True(vm.UpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public async Task UpdateYtDlpCommand_CanExecute_FalseWhileSharedUpdaterUpdatingFromElsewhere()
    {
        // Simulates SettingsViewModel starting a -U on the same singleton YtDlpUpdater: this VM's
        // own IsUpdatingYtDlp never flips, but the command must still be disabled.
        var (vm, _, updater, runner) = CreateVm();
        runner.SelfUpdateGate = new TaskCompletionSource<bool>();

        var task = updater.SelfUpdateAsync(CancellationToken.None);

        Assert.True(updater.IsUpdating);
        Assert.False(vm.UpdateYtDlpCommand.CanExecute(null));
        Assert.False(vm.IsUpdatingYtDlp); // this VM never started it

        runner.SelfUpdateGate.SetResult(true);
        await task;

        Assert.True(vm.UpdateYtDlpCommand.CanExecute(null));
    }

    // --- fakes (mirrors MainViewModelYtDlpBannerTests) ---

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

    /// <summary>Bare-bones <see cref="IYtDlpRunner"/> for the "-U" call only, with an optional gate
    /// so a test can hold <see cref="YtDlpUpdater.IsUpdating"/> true across an assertion.</summary>
    private sealed class GateRunner : IYtDlpRunner
    {
        public YtDlpRunResult SelfUpdateResult { get; set; } = new(0, false);
        public TaskCompletionSource<bool>? SelfUpdateGate { get; set; }

        public async Task<YtDlpRunResult> RunAsync(
            string ytDlpPath,
            IReadOnlyList<string> args,
            string? workingDirectory,
            Action<string> onLine,
            CancellationToken ct)
        {
            if (args.Contains("-U"))
            {
                if (SelfUpdateGate is not null) await SelfUpdateGate.Task.ConfigureAwait(false);
                return SelfUpdateResult;
            }
            return new YtDlpRunResult(0, false);
        }
    }
}

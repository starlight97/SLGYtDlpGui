using System.IO;
using System.Linq;
using Xunit;
using YtDlpGui.Models;
using YtDlpGui.Services;
using YtDlpGui.ViewModels;

namespace YtDlpGui.Tests;

/// <summary>
/// Reverse guard + cross-VM requery follow-up: <see cref="SettingsViewModel"/>'s "Self Update" yt-dlp
/// button must never run alongside an app-update download or a scheduled restart-to-apply, and must
/// respect <see cref="YtDlpUpdater.IsUpdating"/> when a -U was started from the OTHER live view model
/// (the singleton MainViewModel banner) sharing the same singleton <see cref="YtDlpUpdater"/>.
/// </summary>
public class SettingsViewModelAppUpdateGuardTests : IDisposable
{
    private readonly string _fakeExePath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_fakeExePath); } catch { /* best effort */ }
    }

    private (SettingsViewModel vm, FakeAppUpdateService appUpdates, YtDlpUpdater updater, GateRunner runner) CreateVm()
    {
        var runner = new GateRunner();
        var resolver = new FakeResolver(_fakeExePath);
        var updater = new YtDlpUpdater(resolver, runner);
        var settings = new FakeSettingsStore();
        var options = new OptionsViewModel(settings, new FakeProfileStore());
        var appUpdates = new FakeAppUpdateService { IsInstalled = true, Status = new AppUpdateStatus(AppUpdateState.Idle, "0.4.0") };
        var vm = new SettingsViewModel(
            settings,
            new FakeConfImporter(),
            options,
            resolver,
            updater,
            appUpdates);
        return (vm, appUpdates, updater, runner);
    }

    [Fact]
    public void SelfUpdateCommand_CanExecute_FalseWhileAppUpdateDownloading()
    {
        var (vm, appUpdates, _, _) = CreateVm();
        Assert.True(vm.SelfUpdateYtDlpCommand.CanExecute(null));

        appUpdates.Status = new AppUpdateStatus(AppUpdateState.Downloading, "0.4.0", "0.5.0", 10);

        Assert.False(vm.SelfUpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public void SelfUpdateCommand_CanExecute_TrueAgainAfterAppUpdateReturnsToIdle()
    {
        var (vm, appUpdates, _, _) = CreateVm();
        appUpdates.Status = new AppUpdateStatus(AppUpdateState.Downloading, "0.4.0", "0.5.0", 10);
        Assert.False(vm.SelfUpdateYtDlpCommand.CanExecute(null));

        appUpdates.Status = new AppUpdateStatus(AppUpdateState.Idle, "0.4.0");

        Assert.True(vm.SelfUpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public void SelfUpdateCommand_CanExecute_FalseWhileApplyScheduled()
    {
        var (vm, appUpdates, _, _) = CreateVm();
        appUpdates.IsApplyScheduled = true;

        Assert.False(vm.SelfUpdateYtDlpCommand.CanExecute(null));

        appUpdates.IsApplyScheduled = false;
        Assert.True(vm.SelfUpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelfUpdateCommand_CanExecute_FalseWhileSharedUpdaterUpdatingFromElsewhere()
    {
        // Simulates MainViewModel's banner starting a -U on the same singleton YtDlpUpdater: this
        // VM's own IsUpdatingYtDlp never flips, but the command must still be disabled.
        var (vm, _, updater, runner) = CreateVm();
        runner.SelfUpdateGate = new TaskCompletionSource<bool>();

        var task = updater.SelfUpdateAsync(CancellationToken.None);

        Assert.True(updater.IsUpdating);
        Assert.False(vm.SelfUpdateYtDlpCommand.CanExecute(null));
        Assert.False(vm.IsUpdatingYtDlp);

        runner.SelfUpdateGate.SetResult(true);
        await task;

        Assert.True(vm.SelfUpdateYtDlpCommand.CanExecute(null));
    }

    [Fact]
    public async Task OnWindowClosed_UnsubscribesFromYtDlpUpdater()
    {
        // If the unsubscribe in OnWindowClosed were missing/wrong, this closed VM would still react
        // to IsUpdatingChanged; there's no observable-from-outside way to assert "did nothing", so
        // this just pins that OnWindowClosed can be called safely after the VM already reacted once.
        var (vm, _, updater, runner) = CreateVm();
        vm.OnWindowClosed();

        runner.SelfUpdateGate = new TaskCompletionSource<bool>();
        var task = updater.SelfUpdateAsync(CancellationToken.None);
        runner.SelfUpdateGate.SetResult(true);

        // Must not throw even though the VM unsubscribed before this ran.
        var result = await task;
        Assert.True(result.Success);
    }

    [Fact]
    public void OnWindowClosed_UnsubscribesFromAppUpdateService_StatusChangedNoLongerReachesVm()
    {
        var (vm, appUpdates, _, _) = CreateVm();
        Assert.True(appUpdates.HasStatusChangedSubscribers);

        vm.OnWindowClosed();

        Assert.False(appUpdates.HasStatusChangedSubscribers);

        // Must not throw even though nothing is listening anymore.
        appUpdates.RaiseStatusChanged(new AppUpdateStatus(AppUpdateState.Available, "0.4.0", "0.5.0"));
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

    private sealed class FakeConfImporter : IConfImporter
    {
        public ConfImportResult Import(string filePath) => ImportText(string.Empty);
        public ConfImportResult ImportText(string text) => new(new DownloadOptions(), Array.Empty<string>(), Array.Empty<string>());
    }

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

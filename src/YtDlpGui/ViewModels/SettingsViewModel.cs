using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using YtDlpGui.Infrastructure;
using YtDlpGui.Services;
using YtDlpGui.Views;

namespace YtDlpGui.ViewModels;

/// <summary>
/// Bound to <c>SettingsWindow</c>. Holds a working copy of the persisted
/// settings; only commits to <see cref="ISettingsStore"/> on Apply, so Cancel
/// is a true no-op. The Conf importer (SPEC §4.1) and Log folder utilities
/// live here too — anything that's "out-of-band" of the per-download Options.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly IConfImporter _importer;
    private readonly OptionsViewModel _options;
    private readonly IBinaryResolver _binaries;
    private readonly YtDlpUpdater _updater;
    private readonly IAppUpdateService _appUpdates;
    private readonly string _originalTheme;

    public SettingsViewModel(
        ISettingsStore store,
        IConfImporter importer,
        OptionsViewModel options,
        IBinaryResolver binaries,
        YtDlpUpdater updater,
        IAppUpdateService appUpdates)
    {
        _store = store;
        _importer = importer;
        _options = options;
        _binaries = binaries;
        _updater = updater;
        _appUpdates = appUpdates;
        var s = store.Current;
        _ytDlpPathOverride = s.YtDlpPathOverride;
        _ffmpegPathOverride = s.FfmpegPathOverride;
        // Show the persisted setting if any, otherwise mirror what the Options
        // panel currently has so the user isn't faced with a surprise default.
        _defaultOutputFolder = string.IsNullOrEmpty(s.DefaultOutputFolder)
            ? options.OutputFolder
            : s.DefaultOutputFolder;
        _parallelism = s.Parallelism;
        _theme = string.IsNullOrEmpty(s.Theme) ? "System" : s.Theme;
        _originalTheme = _theme;
        SettingsFilePath = store.SettingsFilePath;
        LogFolderPath = Path.Combine(
            Path.GetDirectoryName(SettingsFilePath) ?? string.Empty,
            "logs");
        AppVersion = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
        BinFolderPath = BinaryResolver.GetManagedBinDirectory(store);

        _appUpdates.StatusChanged += OnAppUpdateStatusChanged;
        ApplyAppUpdateStatus(_appUpdates.Status);

        // YtDlpUpdater.IsUpdating is a singleton flag also flipped by the singleton MainViewModel's
        // banner Update button — this transient VM must re-query its own commands when that happens.
        _updater.IsUpdatingChanged += OnYtDlpUpdaterIsUpdatingChanged;
    }

    /// <summary>Live-preview the theme as the user changes the dropdown.</summary>
    partial void OnThemeChanged(string value)
    {
        try { ThemeApplier.Apply(value); } catch { /* tolerate WPF-UI hiccups */ }
    }

    public string SettingsFilePath { get; }
    public string LogFolderPath { get; }
    public string AppVersion { get; }
    public string BinFolderPath { get; }

    [ObservableProperty] private string _ytDlpVersion = "(click Check)";
    [ObservableProperty] private string _ffmpegVersion = "(click Check)";
    [ObservableProperty] private bool _isUpdatingYtDlp;
    [ObservableProperty] private string _selfUpdateStatus = string.Empty;

    // --- App self-update (Velopack / GitHub Releases) ---
    [ObservableProperty] private string _appUpdateStatusText = string.Empty;
    [ObservableProperty] private int _appUpdateProgress;
    [ObservableProperty] private bool _isAppUpdateDownloading;
    [ObservableProperty] private bool _isAppUpdateAvailable;
    [ObservableProperty] private bool _isAppUpdateReady;

    partial void OnIsUpdatingYtDlpChanged(bool value)
    {
        SelfUpdateYtDlpCommand.NotifyCanExecuteChanged();
        RestartToUpdateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Called from SettingsWindow.Closed — the services outlive this transient VM.</summary>
    public void OnWindowClosed()
    {
        _appUpdates.StatusChanged -= OnAppUpdateStatusChanged;
        _updater.IsUpdatingChanged -= OnYtDlpUpdaterIsUpdatingChanged;
    }

    // Raised on a background thread; always render the latest snapshot.
    private void OnAppUpdateStatusChanged(AppUpdateStatus _)
        => Application.Current?.Dispatcher.BeginInvoke(() => ApplyAppUpdateStatus(_appUpdates.Status));

    /// <summary>Marshaled like <see cref="OnAppUpdateStatusChanged"/>: fired on whatever thread MainViewModel's
    /// (or this VM's own) yt-dlp -U call runs on. SelfUpdate, RestartToUpdate and DownloadAppUpdate are all
    /// guarded by _updater.IsUpdating.</summary>
    private void OnYtDlpUpdaterIsUpdatingChanged()
        => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            SelfUpdateYtDlpCommand.NotifyCanExecuteChanged();
            RestartToUpdateCommand.NotifyCanExecuteChanged();
            DownloadAppUpdateCommand.NotifyCanExecuteChanged();
        });

    private void ApplyAppUpdateStatus(AppUpdateStatus s)
    {
        AppUpdateStatusText = DescribeAppUpdate(s);
        AppUpdateProgress = s.Progress;
        IsAppUpdateDownloading = s.State == AppUpdateState.Downloading;
        IsAppUpdateAvailable = s.State == AppUpdateState.Available;
        IsAppUpdateReady = s.State == AppUpdateState.ReadyToRestart;
        CheckAppUpdateCommand.NotifyCanExecuteChanged();
        DownloadAppUpdateCommand.NotifyCanExecuteChanged();
        RestartToUpdateCommand.NotifyCanExecuteChanged();
        SelfUpdateYtDlpCommand.NotifyCanExecuteChanged(); // Downloading/apply-scheduled also gate yt-dlp's own -U.
    }

    private static string DescribeAppUpdate(AppUpdateStatus s) => s.State switch
    {
        AppUpdateState.NotInstalled =>
            "Automatic updates work only in the installed app (Setup.exe). This copy runs from a build/unzipped folder.",
        AppUpdateState.Idle => $"Current version {s.CurrentVersion}.",
        AppUpdateState.Checking => "Checking GitHub for a new version…",
        AppUpdateState.UpToDate => $"You're up to date ({s.CurrentVersion}).",
        AppUpdateState.Available => $"Version {s.AvailableVersion} is available (current {s.CurrentVersion}).",
        AppUpdateState.Downloading => $"Downloading {s.AvailableVersion}… {s.Progress}%",
        AppUpdateState.ReadyToRestart =>
            $"Version {s.AvailableVersion} is ready. Restart to finish updating — otherwise it is applied the next time YtDlpGui starts.",
        AppUpdateState.Failed => $"App update failed: {s.Error}",
        _ => string.Empty,
    };

    private bool CanCheckAppUpdate() => _appUpdates.IsInstalled
        && _appUpdates.Status.State is not (AppUpdateState.Checking or AppUpdateState.Downloading or AppUpdateState.ReadyToRestart);

    [RelayCommand(CanExecute = nameof(CanCheckAppUpdate))]
    private Task CheckAppUpdateAsync() => _appUpdates.CheckAsync();

    // Must never race a yt-dlp -U self-replace: DownloadUpdatesAsync writes into the install folder,
    // the same hazard CanSelfUpdate already guards against in the other direction.
    private bool CanDownloadAppUpdate() => _appUpdates.Status.State == AppUpdateState.Available && !_updater.IsUpdating;

    [RelayCommand(CanExecute = nameof(CanDownloadAppUpdate))]
    private Task DownloadAppUpdateAsync() => _appUpdates.DownloadAsync();

    private bool CanRestartToUpdate() => _appUpdates.Status.State == AppUpdateState.ReadyToRestart && !_updater.IsUpdating;

    [RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
    private void RestartToUpdate()
    {
        if (!AppUpdateRestart.ConfirmAndSchedule(_appUpdates, ActiveOwner())) return;
        SelfUpdateYtDlpCommand.NotifyCanExecuteChanged(); // IsApplyScheduled just flipped true

        Cancel(); // close Settings like the Cancel button (reverts theme preview; unsaved edits are dropped)

        // Posted so it runs after the modal Settings loop has unwound.
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => AppUpdateRestart.CloseMainWindow(_appUpdates)));
    }

    [RelayCommand]
    private void OpenBinFolder()
    {
        try { Directory.CreateDirectory(BinFolderPath); } catch { /* surfaced if open fails */ }
        OpenInExplorer(BinFolderPath);
    }

    [ObservableProperty] private string _ytDlpPathOverride;
    [ObservableProperty] private string _ffmpegPathOverride;
    [ObservableProperty] private string _defaultOutputFolder;
    [ObservableProperty] private int _parallelism;
    [ObservableProperty] private string _theme;

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    [RelayCommand]
    private void BrowseYtDlp() => PickFile(p => YtDlpPathOverride = p, YtDlpPathOverride, "yt-dlp.exe");

    [RelayCommand]
    private void BrowseFfmpeg() => PickFile(p => FfmpegPathOverride = p, FfmpegPathOverride, "ffmpeg.exe");

    [RelayCommand]
    private void BrowseDefaultOutputFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Default output folder",
            InitialDirectory = Directory.Exists(DefaultOutputFolder) ? DefaultOutputFolder : string.Empty,
        };
        if (dlg.ShowDialog() == true)
            DefaultOutputFolder = dlg.FolderName;
    }

    [RelayCommand]
    private void OpenSettingsFolder() => OpenInExplorer(Path.GetDirectoryName(SettingsFilePath));

    [RelayCommand]
    private void OpenLogFolder()
    {
        try { Directory.CreateDirectory(LogFolderPath); } catch { /* surfaced if open fails */ }
        OpenInExplorer(LogFolderPath);
    }

    [RelayCommand]
    private void ImportConf()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import yt-dlp config",
            Filter = "Config files (*.conf;*.txt)|*.conf;*.txt|All files (*.*)|*.*",
        };
        // SPEC §4.1: default to the portable location next to yt-dlp.exe.
        var seedDir = SeedConfDirectory();
        if (!string.IsNullOrEmpty(seedDir)) dlg.InitialDirectory = seedDir;

        if (dlg.ShowDialog() != true) return;

        ConfImportResult result;
        try
        {
            result = _importer.Import(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ActiveOwner(),
                $"Couldn't read config file:\n{ex.Message}",
                "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var summary = new ConfImportDialog(result, dlg.FileName)
        {
            Owner = ActiveOwner(),
        };
        if (summary.ShowDialog() == true)
        {
            _options.ApplyOptions(result.Options);
        }
    }

    /// <summary>SPEC §5.6: query <c>yt-dlp --version</c> and <c>ffmpeg -version</c> for the About panel.</summary>
    [RelayCommand]
    private async Task CheckVersionsAsync()
    {
        YtDlpVersion = "(checking…)";
        FfmpegVersion = "(checking…)";

        var ytDlp = _binaries.ResolveYtDlp();
        YtDlpVersion = ytDlp is null
            ? "(not found)"
            : await GetExeVersionAsync(ytDlp, new[] { "--version" }).ConfigureAwait(true);

        var ffmpeg = _binaries.ResolveFfmpeg();
        FfmpegVersion = ffmpeg is null
            ? "(not found)"
            : await GetExeVersionAsync(ffmpeg, new[] { "-version" }).ConfigureAwait(true);
    }

    // Must never race an app-update download/apply: Velopack writes into the install folder while
    // downloading, and force-kills every process under it once a restart-to-apply is scheduled/in progress.
    private bool CanSelfUpdate() => !IsUpdatingYtDlp && !_updater.IsUpdating
        && _appUpdates.Status.State != AppUpdateState.Downloading && !_appUpdates.IsApplyScheduled;

    /// <summary>SPEC §5.6: in-place upgrade via <c>yt-dlp -U</c>, delegated to <see cref="YtDlpUpdater"/>
    /// so the banner's Update button and this one share the same "only one -U at a time" guard.</summary>
    [RelayCommand(CanExecute = nameof(CanSelfUpdate))]
    private async Task SelfUpdateYtDlpAsync()
    {
        IsUpdatingYtDlp = true;
        SelfUpdateStatus = "Running yt-dlp -U…";
        try
        {
            var r = await _updater.SelfUpdateAsync(CancellationToken.None).ConfigureAwait(true);
            SelfUpdateStatus = r.Success ? $"Done.\n{r.Output}" : $"{r.Message}\n{r.Output}";
        }
        catch (Exception ex)
        {
            // YtDlpUpdater.SelfUpdateAsync never throws, but AsyncRelayCommand re-throws unhandled
            // exceptions on the UI context and this app has no DispatcherUnhandledException handler.
            SelfUpdateStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsUpdatingYtDlp = false;
        }
    }

    private static async Task<string> GetExeVersionAsync(string exePath, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc is null) return "(launch failed)";

            var stdout = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await proc.WaitForExitAsync().ConfigureAwait(false);

            // ffmpeg writes its banner to stderr; yt-dlp writes the version to stdout.
            var combined = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            var firstLine = combined.Split('\n').FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(firstLine) ? "(empty)" : firstLine!;
        }
        catch (Exception ex)
        {
            return $"(error: {ex.Message})";
        }
    }

    [RelayCommand]
    private void Apply()
    {
        var s = _store.Current;
        s.YtDlpPathOverride = (YtDlpPathOverride ?? string.Empty).Trim();
        s.FfmpegPathOverride = (FfmpegPathOverride ?? string.Empty).Trim();
        var folder = (DefaultOutputFolder ?? string.Empty).Trim();
        s.DefaultOutputFolder = folder;
        s.Parallelism = Math.Clamp(Parallelism, 1, 32);
        s.Theme = string.IsNullOrEmpty(Theme) ? "System" : Theme;
        _store.Save();

        // Push to the live Options panel so the change takes effect immediately
        // and the next "Add to queue" uses it. (LastOptions persistence on exit
        // will then capture this in the per-session record too.)
        if (!string.IsNullOrEmpty(folder)) _options.OutputFolder = folder;

        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        // Revert any live theme preview the user made before pressing Cancel.
        if (!string.Equals(Theme, _originalTheme, StringComparison.Ordinal))
        {
            try { ThemeApplier.Apply(_originalTheme); } catch { /* tolerate WPF-UI hiccups */ }
        }
        DialogResult = false;
        RequestClose?.Invoke();
    }

    private string? SeedConfDirectory()
    {
        if (!string.IsNullOrWhiteSpace(YtDlpPathOverride))
        {
            var dir = Path.GetDirectoryName(YtDlpPathOverride);
            if (Directory.Exists(dir)) return dir;
        }
        // Same lookup the downloader uses (override → tools folder → next to app → PATH).
        var ytDlp = _binaries.ResolveYtDlp();
        var resolvedDir = string.IsNullOrEmpty(ytDlp) ? null : Path.GetDirectoryName(ytDlp);
        return Directory.Exists(resolvedDir) ? resolvedDir : null;
    }

    private static Window? ActiveOwner()
    {
        if (Application.Current is null) return null;
        // Prefer the currently-active window so dialogs center over what the user is looking at.
        return Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;
    }

    private static void OpenInExplorer(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try { System.Diagnostics.Process.Start("explorer.exe", folder); } catch { /* not critical */ }
    }

    private static void PickFile(Action<string> assign, string? current, string defaultName)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Locate {defaultName}",
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = !string.IsNullOrWhiteSpace(current) && File.Exists(current)
                ? current
                : defaultName,
        };
        if (!string.IsNullOrWhiteSpace(current))
        {
            var dir = Path.GetDirectoryName(current);
            if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
        }
        if (dlg.ShowDialog() == true) assign(dlg.FileName);
    }
}

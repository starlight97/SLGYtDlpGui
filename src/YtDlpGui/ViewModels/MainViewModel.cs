using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YtDlpGui.Infrastructure;
using YtDlpGui.Models;
using YtDlpGui.Services;
using YtDlpGui.Views;

namespace YtDlpGui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s'""<>]+", RegexOptions.Compiled);

    private readonly IBinaryResolver _binaries;
    private readonly IDownloadQueue _queue;
    private readonly IYtDlpRunner _runner;
    private readonly ISettingsStore _settings;
    private readonly IServiceProvider _services;
    private readonly YtDlpUpdater _ytDlpUpdater;
    private readonly IAppUpdateService _appUpdates;

    private List<string> _clipboardCandidates = new();

    // --- Outdated yt-dlp banner bookkeeping ---
    private string? _outdatedYtDlpVersion;   // version the banner currently describes
    private string? _dismissedYtDlpVersion;  // [x] hides the banner for this version for the session
    private int _versionCheckSeq;            // UI-thread only; drops stale results when checks overlap

    // True while Validate (--simulate) or the Format Inspector dialog has a yt-dlp.exe process of
    // its own running — a "yt-dlp -U" self-replace must never race against either one.
    private bool _isRunningAdhocYtDlp;

    // --- App self-update banner bookkeeping ---
    private bool _startupUpdateCheckStarted;
    private bool _appUpdateBannerDismissed;
    private bool _appUpdateBannerEngaged;   // true once the user clicked the banner's action button

    public MainViewModel(
        IBinaryResolver binaries,
        IDownloadQueue queue,
        IYtDlpRunner runner,
        OptionsViewModel options,
        ISettingsStore settings,
        IServiceProvider services,
        YtDlpUpdater ytDlpUpdater,
        IAppUpdateService appUpdates)
    {
        _binaries = binaries;
        _queue = queue;
        _runner = runner;
        _settings = settings;
        _services = services;
        _ytDlpUpdater = ytDlpUpdater;
        _appUpdates = appUpdates;
        Options = options;

        YtDlpPath = _binaries.ResolveYtDlp()
            ?? "(not found — put yt-dlp.exe in the tools folder (Settings > About) or on PATH)";

        _queue.Parallelism = settings.Current.Parallelism;
        _parallelism = _queue.Parallelism;

        _queue.StatusChanged += OnStatusChanged;
        _queue.ProgressUpdated += OnProgressUpdated;
        _queue.LogLine += OnLogLine;
        _queue.Finished += OnFinished;

        _appUpdates.StatusChanged += OnAppUpdateStatusChanged;
        // A package downloaded in an earlier session (ReadyToRestart) must show up immediately.
        ApplyAppUpdateBanner(_appUpdates.Status);

        // YtDlpUpdater.IsUpdating is a singleton flag also flipped by the transient SettingsViewModel's
        // Self Update button — this VM (itself a singleton, so no matching unsubscribe) must re-query
        // UpdateYtDlpCommand when that happens, not just when its own IsUpdatingYtDlp changes.
        _ytDlpUpdater.IsUpdatingChanged += OnYtDlpUpdaterIsUpdatingChanged;
    }

    public OptionsViewModel Options { get; }
    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    /// <summary>Bound to MainWindow.Title. Same assembly-version source as SettingsViewModel.AppVersion (ToString(3)).</summary>
    public string WindowTitle { get; } = $"YtDlpGui v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?"}";

    [ObservableProperty] private string _ytDlpPath;
    [ObservableProperty] private string _urlsInput = string.Empty;
    [ObservableProperty] private string _statusText = "Idle.";

    [ObservableProperty] private int _parallelism;
    partial void OnParallelismChanged(int value) => _queue.Parallelism = value;

    // --- Clipboard auto-detect (SPEC §5.1) ---
    [ObservableProperty] private bool _hasClipboardCandidates;
    [ObservableProperty] private string _clipboardBannerText = string.Empty;

    // --- Outdated yt-dlp banner ---
    [ObservableProperty] private bool _hasOutdatedYtDlp;
    [ObservableProperty] private string _ytDlpBannerText = string.Empty;
    [ObservableProperty] private string _ytDlpBannerDetail = string.Empty;
    [ObservableProperty] private bool _isUpdatingYtDlp;

    public bool HasYtDlpBannerDetail => !string.IsNullOrEmpty(YtDlpBannerDetail);
    partial void OnYtDlpBannerDetailChanged(string value) => OnPropertyChanged(nameof(HasYtDlpBannerDetail));

    partial void OnIsUpdatingYtDlpChanged(bool value)
    {
        UpdateYtDlpCommand.NotifyCanExecuteChanged();
        DismissYtDlpBannerCommand.NotifyCanExecuteChanged();
        AddToQueueCommand.NotifyCanExecuteChanged();
        OpenSettingsCommand.NotifyCanExecuteChanged();
        UpdateAppCommand.NotifyCanExecuteChanged();
    }

    // --- App self-update banner (Velopack / GitHub Releases) ---
    [ObservableProperty] private bool _hasAppUpdateBanner;
    [ObservableProperty] private string _appUpdateBannerText = string.Empty;
    [ObservableProperty] private string _appUpdateBannerActionText = "Update & restart";
    [ObservableProperty] private int _appUpdateBannerProgress;
    [ObservableProperty] private bool _isAppUpdateBannerDownloading;
    [ObservableProperty] private bool _showAppUpdateBannerAction;

    /// <summary>True once "Restart to update"/banner scheduled the post-exit hand-off; read by MainWindow.OnClosing.</summary>
    public bool IsRestartingToUpdate => _appUpdates.IsApplyScheduled;

    /// <summary>
    /// True while a yt-dlp -U is in flight, whether it was started from this VM or from
    /// SettingsViewModel (a transient VM whose own IsUpdatingYtDlp never reaches this singleton).
    /// Read by MainWindow.OnClosing so the "yt-dlp update in progress" warning covers both paths.
    /// </summary>
    public bool IsYtDlpSelfUpdateActive => IsUpdatingYtDlp || _ytDlpUpdater.IsUpdating;

    /// <summary>True if any item in the queue is still in flight. Evaluated on demand (not bound).</summary>
    public bool HasActiveDownloads => Items.Any(v => !v.IsTerminal);
    public int ActiveCount => Items.Count(v => !v.IsTerminal);

    private bool CanAdd() => !string.IsNullOrWhiteSpace(UrlsInput) && !IsUpdatingYtDlp;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddToQueue()
    {
        var urls = UrlsInput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (urls.Count == 0) return;

        var snap = Options.Snapshot();
        try { Directory.CreateDirectory(snap.OutputFolder); } catch { /* surfaced per-item later */ }

        foreach (var url in urls) EnqueueOne(url, snap);

        UrlsInput = string.Empty;
        StatusText = $"Enqueued {urls.Count}.";
    }

    [RelayCommand]
    private void CancelAll() => _queue.CancelAll();

    [RelayCommand]
    private void ClearDone()
    {
        var toRemove = Items.Where(v => v.IsTerminal).ToList();
        foreach (var v in toRemove)
        {
            if (!_queue.IsInFlight(v.Id)) Items.Remove(v);
        }
    }

    [RelayCommand]
    private void RemoveItem(DownloadItemViewModel? vm)
    {
        if (vm is null) return;
        if (_queue.IsInFlight(vm.Id)) return;
        Items.Remove(vm);
    }

    [RelayCommand]
    private void RetryItem(DownloadItemViewModel? vm)
    {
        if (vm is null || !vm.IsTerminal || IsUpdatingYtDlp) return;
        EnqueueOne(vm.Item.Url, vm.Item.Options);
    }

    [RelayCommand]
    private void RetryFailed()
    {
        if (IsUpdatingYtDlp) { StatusText = "Wait for the yt-dlp update to finish."; return; }

        // Snapshot Items first; EnqueueOne mutates the collection.
        var failed = Items.Where(v => v.Status is DownloadStatus.Failed or DownloadStatus.Canceled).ToList();
        foreach (var v in failed) EnqueueOne(v.Item.Url, v.Item.Options);
        if (failed.Count > 0) StatusText = $"Re-queued {failed.Count}.";
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var folder = Options.OutputFolder;
        if (Directory.Exists(folder))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", folder); }
            catch { /* not critical */ }
        }
    }

    private bool CanOpenSettings() => !IsUpdatingYtDlp;

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private void OpenSettings()
    {
        var vm = _services.GetRequiredService<SettingsViewModel>();
        var win = new SettingsWindow(vm) { Owner = Application.Current?.MainWindow };
        var saved = win.ShowDialog();
        if (saved == true)
        {
            YtDlpPath = _binaries.ResolveYtDlp()
                ?? "(not found — put yt-dlp.exe in the tools folder (Settings > About) or on PATH)";
            Parallelism = _settings.Current.Parallelism;
        }
        // Re-check regardless of Save/Cancel: the path override or a Settings-driven -U
        // may have changed what's on disk either way.
        _ = CheckYtDlpVersionAsync();
    }

    [RelayCommand]
    private void OpenInspector()
    {
        if (IsUpdatingYtDlp) { StatusText = "Wait for the yt-dlp update to finish."; return; }

        var vm = _services.GetRequiredService<FormatPickerViewModel>();
        vm.Url = FirstAvailableUrl();
        var win = new FormatPickerWindow(vm) { Owner = Application.Current?.MainWindow };

        // The dialog can (re-)run yt-dlp -F for as long as it's open — keep the Update button
        // disabled for its whole lifetime, not just around a single inspect call.
        _isRunningAdhocYtDlp = true;
        UpdateYtDlpCommand.NotifyCanExecuteChanged();
        try
        {
            var applied = win.ShowDialog();
            if (applied == true && !string.IsNullOrEmpty(vm.Result))
            {
                Options.FormatSelector = vm.Result;
            }
        }
        finally
        {
            _isRunningAdhocYtDlp = false;
            UpdateYtDlpCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// SPEC §13: validate a hand-written -f selector with <c>yt-dlp --simulate</c>
    /// before the user enqueues against it.
    /// </summary>
    [RelayCommand]
    private async Task ValidateFormatAsync()
    {
        if (IsUpdatingYtDlp) { StatusText = "Validate: wait for the yt-dlp update to finish."; return; }

        var url = FirstAvailableUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText = "Validate: paste a URL first.";
            return;
        }
        var selector = Options.FormatSelector;
        if (string.IsNullOrWhiteSpace(selector))
        {
            StatusText = "Validate: format selector is empty.";
            return;
        }
        var ytDlpPath = _binaries.ResolveYtDlp();
        if (ytDlpPath is null || !File.Exists(ytDlpPath))
        {
            StatusText = "Validate: yt-dlp.exe not found. Set the path in Settings.";
            return;
        }

        StatusText = "Validating…";
        _isRunningAdhocYtDlp = true;
        UpdateYtDlpCommand.NotifyCanExecuteChanged();
        try
        {
            var sb = new StringBuilder();
            var result = await _runner.RunAsync(
                ytDlpPath,
                new[] { "--ignore-config", "--no-color", "--no-warnings", "--simulate", "-f", selector, url },
                workingDirectory: null,
                onLine: line => sb.AppendLine(line),
                ct: CancellationToken.None).ConfigureAwait(true);

            if (result.ExitCode == 0 && !result.SawErrorPrefix)
            {
                StatusText = $"Validate: ✓ '{selector}' resolves on {Truncate(url, 50)}.";
            }
            else
            {
                var lastErr = sb.ToString()
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                    ?.Trim() ?? $"exit {result.ExitCode}";
                StatusText = $"Validate: ✗ {Truncate(lastErr, 200)}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Validate: failed ({ex.Message})";
        }
        finally
        {
            _isRunningAdhocYtDlp = false;
            UpdateYtDlpCommand.NotifyCanExecuteChanged();
        }
    }

    // --- Clipboard auto-detect ---

    /// <summary>Called from MainWindow on Window.Activated.</summary>
    public void OnWindowActivated()
    {
        try
        {
            if (!Clipboard.ContainsText()) { HideClipboardBanner(); return; }
            var text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) { HideClipboardBanner(); return; }

            var current = (UrlsInput ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var found = UrlRegex.Matches(text)
                .Select(m => m.Value.TrimEnd('.', ',', ';', ')', '"', '\''))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(u => !current.Contains(u))
                .ToList();

            if (found.Count == 0) { HideClipboardBanner(); return; }

            _clipboardCandidates = found;
            ClipboardBannerText = found.Count == 1
                ? "1 URL detected on clipboard"
                : $"{found.Count} URLs detected on clipboard";
            HasClipboardCandidates = true;
        }
        catch
        {
            HideClipboardBanner();
        }
    }

    [RelayCommand]
    private void AddClipboard()
    {
        if (_clipboardCandidates.Count == 0) return;
        var existing = UrlsInput ?? string.Empty;
        var separator = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : "\n";
        UrlsInput = existing + separator + string.Join('\n', _clipboardCandidates);
        HideClipboardBanner();
    }

    [RelayCommand]
    private void DismissClipboard() => HideClipboardBanner();

    private void HideClipboardBanner()
    {
        HasClipboardCandidates = false;
        _clipboardCandidates = new();
        ClipboardBannerText = string.Empty;
    }

    // --- Outdated yt-dlp banner ---

    /// <summary>Called from MainWindow.Loaded (startup) and after Settings closes.</summary>
    public async Task CheckYtDlpVersionAsync()
    {
        try
        {
            if (IsUpdatingYtDlp) return;

            var seq = ++_versionCheckSeq;
            var check = await Task.Run(() => _ytDlpUpdater.CheckVersionAsync(CancellationToken.None)).ConfigureAwait(true);

            if (seq != _versionCheckSeq || IsUpdatingYtDlp) return; // superseded by a newer check

            if (check.Version is null || check.ReleaseDate is not DateOnly releaseDate)
            {
                // Path not found or output unparseable — nothing new to show.
                HideYtDlpBanner();
                return;
            }

            if (!YtDlpVersionParser.IsOutdated(releaseDate, Today()))
            {
                HideYtDlpBanner();
            }
            else
            {
                ShowYtDlpBanner(check.Version, YtDlpVersionParser.AgeInDays(releaseDate, Today()));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "yt-dlp version check failed");
        }
    }

    // A yt-dlp -U self-replace must never race an app-update download/apply: block it while the app
    // update is downloading (Velopack writes into the install folder) or a restart-to-apply is
    // scheduled/in progress (Velopack force-kills every process under the install root on apply).
    // _ytDlpUpdater.IsUpdating also covers a -U started from the transient SettingsViewModel, which
    // has no IsUpdatingYtDlp of its own reflected here.
    private bool CanUpdateYtDlp() => !IsUpdatingYtDlp && !HasActiveDownloads && !_isRunningAdhocYtDlp
        && !_ytDlpUpdater.IsUpdating
        && _appUpdates.Status.State != AppUpdateState.Downloading && !_appUpdates.IsApplyScheduled;

    [RelayCommand(CanExecute = nameof(CanUpdateYtDlp))]
    private async Task UpdateYtDlpAsync()
    {
        if (HasActiveDownloads)
        {
            YtDlpBannerDetail = "Finish or cancel active downloads before updating.";
            return;
        }

        IsUpdatingYtDlp = true;
        YtDlpBannerDetail = "Running yt-dlp -U…";
        StatusText = "Updating yt-dlp…";
        try
        {
            var r = await Task.Run(() => _ytDlpUpdater.SelfUpdateAsync(CancellationToken.None)).ConfigureAwait(true);
            if (!r.Success)
            {
                YtDlpBannerDetail = "Update failed: " + Truncate(r.Message, 300);
                StatusText = "yt-dlp update failed.";
                return;
            }

            // Invalidate any check that might still be in flight, then re-probe.
            _versionCheckSeq++;
            var check = await Task.Run(() => _ytDlpUpdater.CheckVersionAsync(CancellationToken.None)).ConfigureAwait(true);

            if (check.Version is null || check.ReleaseDate is not DateOnly releaseDate)
            {
                // -U reported success, but the post-update probe couldn't confirm a version
                // (transient lock/I-O right after the self-replace, path override change, …).
                // Don't claim success and don't touch whatever banner state was already showing.
                YtDlpBannerDetail = $"yt-dlp -U ran, but the new version could not be confirmed: {Truncate(r.Message, 300)}";
                StatusText = "yt-dlp -U ran; version unconfirmed.";
            }
            else if (YtDlpVersionParser.IsOutdated(releaseDate, Today()))
            {
                ShowYtDlpBanner(check.Version, YtDlpVersionParser.AgeInDays(releaseDate, Today()));
                YtDlpBannerDetail = $"yt-dlp -U didn't install a newer build: {Truncate(r.Message, 300)}";
            }
            else
            {
                HideYtDlpBanner();
                StatusText = $"yt-dlp updated to {check.Version}.";
            }
        }
        catch (Exception ex)
        {
            // AsyncRelayCommand re-throws unhandled exceptions on the UI context, and this app
            // has no DispatcherUnhandledException handler — this catch must not be removed.
            Log.Warning(ex, "yt-dlp -U update flow failed unexpectedly");
            YtDlpBannerDetail = $"Update failed: {ex.Message}";
            StatusText = "yt-dlp update failed.";
        }
        finally
        {
            IsUpdatingYtDlp = false;
        }
    }

    private bool CanDismissYtDlpBanner() => !IsUpdatingYtDlp;

    [RelayCommand(CanExecute = nameof(CanDismissYtDlpBanner))]
    private void DismissYtDlpBanner()
    {
        _dismissedYtDlpVersion = _outdatedYtDlpVersion;
        HideYtDlpBanner();
    }

    private void ShowYtDlpBanner(string version, int? ageDays)
    {
        if (string.Equals(version, _dismissedYtDlpVersion, StringComparison.Ordinal)) return;

        // Don't clobber an existing failure detail (e.g. from a just-failed update) when we're
        // simply re-confirming the same outdated version is still showing — but do clear it as
        // soon as the version being described actually changes (e.g. a different override path).
        if (!HasOutdatedYtDlp || !string.Equals(version, _outdatedYtDlpVersion, StringComparison.Ordinal))
            YtDlpBannerDetail = string.Empty;

        _outdatedYtDlpVersion = version;
        YtDlpBannerText = ageDays is int age
            ? $"yt-dlp {version} is {age} days old — YouTube downloads may fail (e.g. HTTP 403)."
            : $"yt-dlp {version} is older than {YtDlpVersionParser.OutdatedAfterDays} days — YouTube downloads may fail (e.g. HTTP 403).";

        if (!HasOutdatedYtDlp) Log.Information("Outdated yt-dlp banner shown: {Version}", version);
        HasOutdatedYtDlp = true;
    }

    private void HideYtDlpBanner()
    {
        HasOutdatedYtDlp = false;
        _outdatedYtDlpVersion = null;
        YtDlpBannerText = string.Empty;
        YtDlpBannerDetail = string.Empty;
    }

    /// <summary>Fallback: yt-dlp's own "is older than 90 days" warning line, seen during a download.</summary>
    private void ReportOutdatedWarning(string version)
    {
        if (HasOutdatedYtDlp || IsUpdatingYtDlp) return;

        var age = YtDlpVersionParser.TryParse(version, out _, out var releaseDate)
            ? YtDlpVersionParser.AgeInDays(releaseDate, Today())
            : (int?)null;
        ShowYtDlpBanner(version, age);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    // --- App self-update banner (Velopack) ---

    /// <summary>Called from MainWindow.OnLoaded next to CheckYtDlpVersionAsync; never blocks first paint.</summary>
    public async Task CheckAppUpdateInBackgroundAsync()
    {
        if (_startupUpdateCheckStarted || !_appUpdates.IsInstalled) return;   // dev/F5: no network call
        _startupUpdateCheckStarted = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false); // let first paint + yt-dlp probe go first
            await _appUpdates.CheckAsync().ConfigureAwait(false);           // banner driven by StatusChanged
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Background app update check failed");
        }
    }

    private void OnAppUpdateStatusChanged(AppUpdateStatus _)
        => Application.Current?.Dispatcher.BeginInvoke(() => ApplyAppUpdateBanner(_appUpdates.Status));

    private void ApplyAppUpdateBanner(AppUpdateStatus s)
    {
        AppUpdateBannerProgress = s.Progress;
        IsAppUpdateBannerDownloading = s.State == AppUpdateState.Downloading;
        ShowAppUpdateBannerAction = s.State is AppUpdateState.Available or AppUpdateState.ReadyToRestart;

        AppUpdateBannerText = s.State switch
        {
            AppUpdateState.Available or AppUpdateState.Downloading => $"YtDlpGui {s.AvailableVersion} is available.",
            AppUpdateState.ReadyToRestart => $"YtDlpGui {s.AvailableVersion} is ready.",
            AppUpdateState.Failed => "App update failed — see Settings > About",
            _ => AppUpdateBannerText,
        };
        AppUpdateBannerActionText = s.State == AppUpdateState.ReadyToRestart ? "Restart now" : "Update & restart";

        HasAppUpdateBanner = !_appUpdateBannerDismissed && s.State switch
        {
            AppUpdateState.Available or AppUpdateState.Downloading or AppUpdateState.ReadyToRestart => true,
            AppUpdateState.Failed => _appUpdateBannerEngaged,
            _ => false,
        };
        UpdateAppCommand.NotifyCanExecuteChanged();
        UpdateYtDlpCommand.NotifyCanExecuteChanged(); // Downloading/ReadyToRestart gate the yt-dlp banner's Update button too.
    }

    /// <summary>Marshaled like <see cref="OnAppUpdateStatusChanged"/>: fired on whatever thread SettingsViewModel's
    /// (or this VM's own) yt-dlp -U call runs on. Both UpdateYtDlp (guarded by _ytDlpUpdater.IsUpdating) and
    /// UpdateApp (guarded the same way, so a Settings-started -U also blocks the app-update banner) depend on it.</summary>
    private void OnYtDlpUpdaterIsUpdatingChanged()
        => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            UpdateYtDlpCommand.NotifyCanExecuteChanged();
            UpdateAppCommand.NotifyCanExecuteChanged();
        });

    private bool CanUpdateApp() => _appUpdates.Status.State is AppUpdateState.Available or AppUpdateState.ReadyToRestart
        && !_ytDlpUpdater.IsUpdating;

    [RelayCommand(CanExecute = nameof(CanUpdateApp))]
    private async Task UpdateAppAsync()   // one click = download + restart (user decision)
    {
        try
        {
            _appUpdateBannerEngaged = true;
            var s = _appUpdates.Status.State == AppUpdateState.ReadyToRestart
                ? _appUpdates.Status
                : await _appUpdates.DownloadAsync().ConfigureAwait(true);
            if (s.State != AppUpdateState.ReadyToRestart) return;

            var scheduled = AppUpdateRestart.ConfirmAndSchedule(_appUpdates, Application.Current?.MainWindow);
            UpdateYtDlpCommand.NotifyCanExecuteChanged(); // IsApplyScheduled just flipped true (or stayed false)
            if (scheduled)
            {
                AppUpdateRestart.CloseMainWindow(_appUpdates);
                // Still here = the user said "No" to a Closing prompt and CloseMainWindow reverted the
                // schedule (CancelScheduledApply) — re-query once more so the button re-enables.
                UpdateYtDlpCommand.NotifyCanExecuteChanged();
            }
        }
        catch (Exception ex)
        {
            // AsyncRelayCommand re-throws unhandled exceptions on the UI context, and this app
            // has no DispatcherUnhandledException handler — this catch must not be removed.
            Log.Warning(ex, "App update flow failed unexpectedly");
            StatusText = "App update failed.";
            UpdateYtDlpCommand.NotifyCanExecuteChanged(); // in case IsApplyScheduled changed before the failure
        }
    }

    [RelayCommand]
    private void DismissAppUpdate()
    {
        _appUpdateBannerDismissed = true;
        HasAppUpdateBanner = false;
    }

    // --- helpers ---

    private void EnqueueOne(string url, DownloadOptions options)
    {
        var item = new DownloadItem { Url = url, Options = options };
        var vm = new DownloadItemViewModel(item, _queue);
        Items.Add(vm);
        _queue.Enqueue(item);
        UpdateYtDlpCommand.NotifyCanExecuteChanged();
    }

    private string FirstAvailableUrl()
    {
        if (!string.IsNullOrWhiteSpace(UrlsInput))
        {
            var first = UrlsInput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .FirstOrDefault(s => s.Length > 0);
            if (!string.IsNullOrEmpty(first)) return first;
        }
        return Items.FirstOrDefault()?.Url ?? string.Empty;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    partial void OnUrlsInputChanged(string value) => AddToQueueCommand.NotifyCanExecuteChanged();

    // --- Queue event routing (called on background threads — marshal to UI). ---

    private DownloadItemViewModel? FindVm(DownloadItem item)
        => Items.FirstOrDefault(v => v.Id == item.Id);

    private void OnStatusChanged(DownloadItem item, DownloadStatus status)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            FindVm(item)?.ApplyStatus(status);
            UpdateYtDlpCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnProgressUpdated(DownloadItem item, ProgressSnapshot snap)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => FindVm(item)?.OnProgress(snap));
    }

    private void OnLogLine(DownloadItem item, string line)
    {
        // Fallback detection for the F1 banner: yt-dlp's own "is older than 90 days" warning.
        // Cheap Contains() check off the UI thread; only regex-matches when it might hit.
        var outdated = YtDlpVersionParser.TryParseOutdatedWarning(line, out var v) ? v : null;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            FindVm(item)?.OnLogLine(line);
            if (outdated is not null) ReportOutdatedWarning(outdated);
        });
    }

    private void OnFinished(DownloadItem item, DownloadFinalResult result)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var inFlight = Items.Count(v => !v.IsTerminal);
            StatusText = inFlight == 0
                ? $"All done. ({Items.Count} items)"
                : $"{inFlight} in flight, {Items.Count - inFlight} finished.";
        });
    }
}

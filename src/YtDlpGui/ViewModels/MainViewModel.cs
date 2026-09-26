using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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

    private List<string> _clipboardCandidates = new();

    public MainViewModel(
        IBinaryResolver binaries,
        IDownloadQueue queue,
        IYtDlpRunner runner,
        OptionsViewModel options,
        ISettingsStore settings,
        IServiceProvider services)
    {
        _binaries = binaries;
        _queue = queue;
        _runner = runner;
        _settings = settings;
        _services = services;
        Options = options;

        YtDlpPath = _binaries.ResolveYtDlp() ?? "(not found — drop yt-dlp.exe next to this app or on PATH)";

        _queue.Parallelism = settings.Current.Parallelism;
        _parallelism = _queue.Parallelism;

        _queue.StatusChanged += OnStatusChanged;
        _queue.ProgressUpdated += OnProgressUpdated;
        _queue.LogLine += OnLogLine;
        _queue.Finished += OnFinished;
    }

    public OptionsViewModel Options { get; }
    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _ytDlpPath;
    [ObservableProperty] private string _urlsInput = string.Empty;
    [ObservableProperty] private string _statusText = "Idle.";

    [ObservableProperty] private int _parallelism;
    partial void OnParallelismChanged(int value) => _queue.Parallelism = value;

    // --- Clipboard auto-detect (SPEC §5.1) ---
    [ObservableProperty] private bool _hasClipboardCandidates;
    [ObservableProperty] private string _clipboardBannerText = string.Empty;

    /// <summary>True if any item in the queue is still in flight. Evaluated on demand (not bound).</summary>
    public bool HasActiveDownloads => Items.Any(v => !v.IsTerminal);
    public int ActiveCount => Items.Count(v => !v.IsTerminal);

    private bool CanAdd() => !string.IsNullOrWhiteSpace(UrlsInput);

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
        if (vm is null || !vm.IsTerminal) return;
        EnqueueOne(vm.Item.Url, vm.Item.Options);
    }

    [RelayCommand]
    private void RetryFailed()
    {
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

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = _services.GetRequiredService<SettingsViewModel>();
        var win = new SettingsWindow(vm) { Owner = Application.Current?.MainWindow };
        var saved = win.ShowDialog();
        if (saved == true)
        {
            YtDlpPath = _binaries.ResolveYtDlp() ?? "(not found — set the path in Settings)";
            Parallelism = _settings.Current.Parallelism;
        }
    }

    [RelayCommand]
    private void OpenInspector()
    {
        var vm = _services.GetRequiredService<FormatPickerViewModel>();
        vm.Url = FirstAvailableUrl();
        var win = new FormatPickerWindow(vm) { Owner = Application.Current?.MainWindow };
        var applied = win.ShowDialog();
        if (applied == true && !string.IsNullOrEmpty(vm.Result))
        {
            Options.FormatSelector = vm.Result;
        }
    }

    /// <summary>
    /// SPEC §13: validate a hand-written -f selector with <c>yt-dlp --simulate</c>
    /// before the user enqueues against it.
    /// </summary>
    [RelayCommand]
    private async Task ValidateFormatAsync()
    {
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

    // --- helpers ---

    private void EnqueueOne(string url, DownloadOptions options)
    {
        var item = new DownloadItem { Url = url, Options = options };
        var vm = new DownloadItemViewModel(item, _queue);
        Items.Add(vm);
        _queue.Enqueue(item);
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
        Application.Current?.Dispatcher.BeginInvoke(() => FindVm(item)?.ApplyStatus(status));
    }

    private void OnProgressUpdated(DownloadItem item, ProgressSnapshot snap)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => FindVm(item)?.OnProgress(snap));
    }

    private void OnLogLine(DownloadItem item, string line)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => FindVm(item)?.OnLogLine(line));
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

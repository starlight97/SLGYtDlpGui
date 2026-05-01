using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtDlpGui.Models;
using YtDlpGui.Services;

namespace YtDlpGui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBinaryResolver _binaries;
    private readonly IDownloadQueue _queue;

    public MainViewModel(IBinaryResolver binaries, IDownloadQueue queue, OptionsViewModel options)
    {
        _binaries = binaries;
        _queue = queue;
        Options = options;

        YtDlpPath = _binaries.ResolveYtDlp() ?? "(not found — drop yt-dlp.exe next to this app or on PATH)";
        _parallelism = queue.Parallelism;

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

        // Snapshot options once so all URLs added in this batch share the same option set.
        var snap = Options.Snapshot();

        try { Directory.CreateDirectory(snap.OutputFolder); } catch { /* surfaced per-item later */ }

        foreach (var url in urls)
        {
            var item = new DownloadItem { Url = url, Options = snap };
            var vm = new DownloadItemViewModel(item, _queue);
            Items.Add(vm);
            _queue.Enqueue(item);
        }

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
    private void OpenOutputFolder()
    {
        var folder = Options.OutputFolder;
        if (Directory.Exists(folder))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", folder); }
            catch { /* not critical */ }
        }
    }

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

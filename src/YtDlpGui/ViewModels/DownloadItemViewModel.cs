using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtDlpGui.Models;
using YtDlpGui.Services;

namespace YtDlpGui.ViewModels;

/// <summary>One row in the queue list. Lifetime is tied to the underlying <see cref="DownloadItem"/>.</summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    private readonly DownloadItem _item;
    private readonly IDownloadQueue _queue;
    private readonly StringBuilder _log = new();
    private int _logLineCount;

    public DownloadItemViewModel(DownloadItem item, IDownloadQueue queue)
    {
        _item = item;
        _queue = queue;
        Url = item.Url;
        Status = DownloadStatus.Queued;
        StatusText = StatusToText(Status);
    }

    public Guid Id => _item.Id;
    public string OutputFolder => _item.Options.OutputFolder;
    /// <summary>Underlying model — exposed so MainViewModel.RetryItem can clone Url+Options.</summary>
    public DownloadItem Item => _item;

    [ObservableProperty] private string _url;
    [ObservableProperty] private DownloadStatus _status;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _percentText = string.Empty;
    [ObservableProperty] private string _speedText = string.Empty;
    [ObservableProperty] private string _etaText = string.Empty;
    [ObservableProperty] private string _fragmentText = string.Empty;
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private bool _isExpanded;

    public bool IsTerminal =>
        Status is DownloadStatus.Done or DownloadStatus.Failed or DownloadStatus.Canceled;

    public bool CanCancel => !IsTerminal;
    public bool CanRemove => IsTerminal;

    [RelayCommand]
    private void Cancel() => _queue.CancelItem(Id);

    [RelayCommand]
    private void OpenContainingFolder()
    {
        // Prefer the actual file (Explorer selects it) when we know the path; fall
        // back to opening the folder.
        var finalPath = _item.FinalFilePath;
        if (!string.IsNullOrEmpty(finalPath) && File.Exists(finalPath))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{finalPath}\""); }
            catch { /* not critical */ }
            return;
        }
        if (Directory.Exists(OutputFolder))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", OutputFolder); }
            catch { /* not critical */ }
        }
    }

    [RelayCommand]
    private void OpenFile()
    {
        var finalPath = _item.FinalFilePath;
        if (!string.IsNullOrEmpty(finalPath) && File.Exists(finalPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(finalPath)
                {
                    UseShellExecute = true,
                });
            }
            catch { /* not critical */ }
            return;
        }
        // No known path — fall back to opening the folder so the user can pick.
        OpenContainingFolder();
    }

    [RelayCommand]
    private void CopyUrl()
    {
        try { System.Windows.Clipboard.SetText(Url); } catch { /* clipboard busy */ }
    }

    public void ApplyStatus(DownloadStatus newStatus)
    {
        Status = newStatus;
        StatusText = StatusToText(newStatus);
        OnPropertyChanged(nameof(IsTerminal));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRemove));
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void OnProgress(ProgressSnapshot snap)
    {
        if (snap.Percent is double p)
        {
            Percent = p;
            PercentText = p.ToString("F1", CultureInfo.InvariantCulture) + "%";
        }
        SpeedText = FormatSpeed(snap.SpeedBytesPerSec);
        EtaText = FormatEta(snap.EtaSeconds);
        FragmentText = (snap.FragmentIndex, snap.FragmentCount) switch
        {
            ({ } i, { } n) => $"{i}/{n}",
            ({ } i, null) => $"{i}",
            _ => string.Empty,
        };
    }

    public void OnLogLine(string line)
    {
        _log.AppendLine(line);
        _logLineCount++;
        // SPEC §5.5: 5000-line cap with cheap front-trim.
        const int max = 5000;
        if (_logLineCount > max)
        {
            var text = _log.ToString();
            var lines = text.Split('\n');
            var keep = lines.Skip(lines.Length - max).ToArray();
            _log.Clear();
            foreach (var l in keep)
            {
                if (l.Length > 0) _log.Append(l);
                _log.Append('\n');
            }
            _logLineCount = max;
        }
        LogText = _log.ToString();
    }

    private static string StatusToText(DownloadStatus s) => s switch
    {
        DownloadStatus.Queued => "Queued",
        DownloadStatus.Resolving => "Resolving…",
        DownloadStatus.Downloading => "Downloading",
        DownloadStatus.Postprocessing => "Postprocessing…",
        DownloadStatus.Done => "Done",
        DownloadStatus.Failed => "Failed",
        DownloadStatus.Canceled => "Canceled",
        _ => s.ToString(),
    };

    private static string FormatSpeed(double? bps)
    {
        if (bps is null or <= 0) return string.Empty;
        var v = bps.Value;
        return v switch
        {
            >= 1_000_000_000 => $"{v / 1_000_000_000:F2} GB/s",
            >= 1_000_000 => $"{v / 1_000_000:F2} MB/s",
            >= 1_000 => $"{v / 1_000:F1} KB/s",
            _ => $"{v:F0} B/s",
        };
    }

    private static string FormatEta(double? seconds)
    {
        if (seconds is null or < 0) return string.Empty;
        var s = (int)Math.Round(seconds.Value);
        if (s >= 3600) return $"{s / 3600}:{(s % 3600) / 60:D2}:{s % 60:D2}";
        return $"{s / 60}:{s % 60:D2}";
    }
}

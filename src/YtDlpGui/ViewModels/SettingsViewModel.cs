using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using YtDlpGui.Services;

namespace YtDlpGui.ViewModels;

/// <summary>
/// Bound to <c>SettingsWindow</c>. Holds a working copy of the persisted
/// settings; only commits to <see cref="ISettingsStore"/> on Apply, so Cancel
/// is a true no-op.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        var s = store.Current;
        _ytDlpPathOverride = s.YtDlpPathOverride;
        _ffmpegPathOverride = s.FfmpegPathOverride;
        _defaultOutputFolder = string.IsNullOrEmpty(s.DefaultOutputFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : s.DefaultOutputFolder;
        _parallelism = s.Parallelism;
        SettingsFilePath = store.SettingsFilePath;
        AppVersion = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
    }

    public string SettingsFilePath { get; }
    public string AppVersion { get; }

    [ObservableProperty] private string _ytDlpPathOverride;
    [ObservableProperty] private string _ffmpegPathOverride;
    [ObservableProperty] private string _defaultOutputFolder;
    [ObservableProperty] private int _parallelism;

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
    private void OpenSettingsFolder()
    {
        var dir = Path.GetDirectoryName(SettingsFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { /* not critical */ }
    }

    [RelayCommand]
    private void Apply()
    {
        var s = _store.Current;
        s.YtDlpPathOverride = (YtDlpPathOverride ?? string.Empty).Trim();
        s.FfmpegPathOverride = (FfmpegPathOverride ?? string.Empty).Trim();
        s.DefaultOutputFolder = (DefaultOutputFolder ?? string.Empty).Trim();
        s.Parallelism = Math.Clamp(Parallelism, 1, 32);
        _store.Save();
        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
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

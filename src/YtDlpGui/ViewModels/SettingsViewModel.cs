using System.IO;
using System.Linq;
using System.Windows;
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
    private readonly string _originalTheme;

    public SettingsViewModel(ISettingsStore store, IConfImporter importer, OptionsViewModel options)
    {
        _store = store;
        _importer = importer;
        _options = options;
        var s = store.Current;
        _ytDlpPathOverride = s.YtDlpPathOverride;
        _ffmpegPathOverride = s.FfmpegPathOverride;
        _defaultOutputFolder = string.IsNullOrEmpty(s.DefaultOutputFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : s.DefaultOutputFolder;
        _parallelism = s.Parallelism;
        _theme = string.IsNullOrEmpty(s.Theme) ? "System" : s.Theme;
        _originalTheme = _theme;
        SettingsFilePath = store.SettingsFilePath;
        LogFolderPath = Path.Combine(
            Path.GetDirectoryName(SettingsFilePath) ?? string.Empty,
            "logs");
        AppVersion = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
    }

    /// <summary>Live-preview the theme as the user changes the dropdown.</summary>
    partial void OnThemeChanged(string value)
    {
        try { ThemeApplier.Apply(value); } catch { /* tolerate WPF-UI hiccups */ }
    }

    public string SettingsFilePath { get; }
    public string LogFolderPath { get; }
    public string AppVersion { get; }

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

    [RelayCommand]
    private void Apply()
    {
        var s = _store.Current;
        s.YtDlpPathOverride = (YtDlpPathOverride ?? string.Empty).Trim();
        s.FfmpegPathOverride = (FfmpegPathOverride ?? string.Empty).Trim();
        s.DefaultOutputFolder = (DefaultOutputFolder ?? string.Empty).Trim();
        s.Parallelism = Math.Clamp(Parallelism, 1, 32);
        s.Theme = string.IsNullOrEmpty(Theme) ? "System" : Theme;
        _store.Save();
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
        // Walk from app exe up to repo root looking for a yt-dlp.exe — same heuristic BinaryResolver uses.
        var here = AppContext.BaseDirectory;
        var d = new DirectoryInfo(here);
        for (var i = 0; i < 6 && d is not null; i++, d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "yt-dlp.exe")))
                return d.FullName;
        }
        return null;
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

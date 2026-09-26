using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using YtDlpGui.Models;
using YtDlpGui.Services;
using YtDlpGui.Views;

namespace YtDlpGui.ViewModels;

/// <summary>
/// View-model for the Options panel. Wraps <see cref="DownloadOptions"/> in full
/// (every field the model supports has a UI binding by v1.0).
///
/// On construction, restores from <see cref="AppSettings.LastOptions"/> if present;
/// otherwise seeds <c>OutputFolder</c> from <see cref="AppSettings.DefaultOutputFolder"/>
/// (and falls back to <c>~/Downloads</c>). The Settings dialog's "Output folder" is
/// authoritative across restarts — see <see cref="ResolveOutputFolder"/>.
///
/// Owns the Profile picker (SPEC §5.2 mode 1) and delegates persistence to
/// <see cref="IProfileStore"/>.
/// </summary>
public sealed partial class OptionsViewModel : ObservableObject
{
    private readonly IProfileStore _profiles;
    private bool _suppressProfileApply;

    public OptionsViewModel(ISettingsStore settings, IProfileStore profiles)
    {
        _profiles = profiles;
        _profiles.Changed += RefreshProfiles;
        RefreshProfiles();

        var saved = settings.Current.LastOptions;
        OutputFolder = ResolveOutputFolder(settings, saved);

        if (saved is not null)
        {
            // --- Output ---
            OutputTemplate = string.IsNullOrEmpty(saved.OutputTemplate) ? "%(title)s.%(ext)s" : saved.OutputTemplate;
            NfcNormalize = saved.NfcNormalize;
            RestrictFilenames = saved.RestrictFilenames;
            WindowsFilenames = saved.WindowsFilenames;

            // --- Format / container ---
            FormatSelector = string.IsNullOrEmpty(saved.FormatSelector) ? _formatSelector : saved.FormatSelector;
            MergeOutputFormat = saved.MergeOutputFormat;
            EmbedMetadata = saved.EmbedMetadata;
            EmbedThumbnail = saved.EmbedThumbnail;
            EmbedChapters = saved.EmbedChapters;
            WriteDescription = saved.WriteDescription;
            WriteInfoJson = saved.WriteInfoJson;

            // --- Subtitles ---
            WriteSubs = saved.WriteSubs;
            WriteAutoSubs = saved.WriteAutoSubs;
            EmbedSubs = saved.EmbedSubs;
            SubLangs = saved.SubLangs;
            ConvertSubsTo = saved.ConvertSubsTo;

            // --- Audio extraction ---
            ExtractAudio = saved.ExtractAudio;
            if (!string.IsNullOrEmpty(saved.AudioCodec)) AudioCodec = saved.AudioCodec;
            if (!string.IsNullOrEmpty(saved.AudioQuality)) AudioQuality = saved.AudioQuality;
            KeepVideo = saved.KeepVideo;

            // --- Network / auth ---
            CookiesFromBrowser = saved.CookiesFromBrowser;
            CookiesBrowserProfile = saved.CookiesBrowserProfile;
            CookiesFile = saved.CookiesFile;
            Proxy = saved.Proxy;
            RateLimit = saved.RateLimit;
            ConcurrentFragments = saved.ConcurrentFragments;
            Retries = saved.Retries;

            // --- Playlist ---
            DownloadPlaylist = saved.DownloadPlaylist;
            PlaylistItems = saved.PlaylistItems;
            PlaylistReverse = saved.PlaylistReverse;
            PlaylistRandom = saved.PlaylistRandom;

            // --- Misc ---
            ForceOverwrites = saved.ForceOverwrites;
            ContinuePartial = saved.ContinuePartial;
            Verbose = saved.Verbose;
            ExtraArgs = saved.ExtraArgs;
        }
    }

    private static string ResolveOutputFolder(ISettingsStore settings, DownloadOptions? saved)
    {
        if (!string.IsNullOrWhiteSpace(settings.Current.DefaultOutputFolder))
            return settings.Current.DefaultOutputFolder;
        if (!string.IsNullOrWhiteSpace(saved?.OutputFolder))
            return saved!.OutputFolder;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    // --- Profiles ---
    public ObservableCollection<Profile> Profiles { get; } = new();
    [ObservableProperty] private Profile? _currentProfile;

    // --- Output ---
    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private string _outputTemplate = "%(title)s.%(ext)s";
    [ObservableProperty] private bool _nfcNormalize = true;
    [ObservableProperty] private bool _restrictFilenames;
    [ObservableProperty] private bool _windowsFilenames;

    // --- Format ---
    [ObservableProperty] private string _formatSelector =
        "bv*[ext=mp4][vcodec^=avc]+ba[ext=m4a]/bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/b";

    /// <summary>"mp4" | "mkv" | "webm" | "" (auto).</summary>
    [ObservableProperty] private string _mergeOutputFormat = "mp4";

    // --- Container / postprocessing ---
    [ObservableProperty] private bool _embedMetadata = true;
    [ObservableProperty] private bool _embedThumbnail = true;
    [ObservableProperty] private bool _embedChapters;
    [ObservableProperty] private bool _writeDescription;
    [ObservableProperty] private bool _writeInfoJson;

    // --- Subtitles ---
    [ObservableProperty] private bool _writeSubs;
    [ObservableProperty] private bool _writeAutoSubs;
    [ObservableProperty] private bool _embedSubs;
    /// <summary>Comma-separated, e.g. "ko,en,en.*".</summary>
    [ObservableProperty] private string _subLangs = string.Empty;
    /// <summary>"" | "srt" | "vtt".</summary>
    [ObservableProperty] private string _convertSubsTo = string.Empty;

    // --- Audio extraction (toggle group) ---
    [ObservableProperty] private bool _extractAudio;
    [ObservableProperty] private string _audioCodec = "mp3";
    [ObservableProperty] private string _audioQuality = "0";
    [ObservableProperty] private bool _keepVideo;

    // --- Network / auth ---
    /// <summary>"" | "chrome" | "firefox" | "edge" | "brave" | ...</summary>
    [ObservableProperty] private string _cookiesFromBrowser = string.Empty;
    [ObservableProperty] private string _cookiesBrowserProfile = string.Empty;
    [ObservableProperty] private string _cookiesFile = string.Empty;
    [ObservableProperty] private string _proxy = string.Empty;
    /// <summary>e.g. "5M". Empty = no limit.</summary>
    [ObservableProperty] private string _rateLimit = string.Empty;
    [ObservableProperty] private int? _concurrentFragments;
    [ObservableProperty] private int? _retries;

    // --- Playlist ---
    /// <summary>true = --yes-playlist, false = --no-playlist, null = yt-dlp default.</summary>
    [ObservableProperty] private bool? _downloadPlaylist;
    /// <summary>e.g. "1-5,8".</summary>
    [ObservableProperty] private string _playlistItems = string.Empty;
    [ObservableProperty] private bool _playlistReverse;
    [ObservableProperty] private bool _playlistRandom;

    // --- Misc ---
    [ObservableProperty] private bool _forceOverwrites;
    [ObservableProperty] private bool _continuePartial = true;
    [ObservableProperty] private bool _verbose;
    [ObservableProperty] private string _extraArgs = string.Empty;

    /// <summary>Produce a snapshot for enqueueing — mutations to this VM after enqueue won't affect the in-flight item.</summary>
    public DownloadOptions Snapshot()
    {
        return new DownloadOptions
        {
            // Output
            OutputFolder = OutputFolder,
            OutputTemplate = OutputTemplate,
            NfcNormalize = NfcNormalize,
            RestrictFilenames = RestrictFilenames,
            WindowsFilenames = WindowsFilenames,

            // Format / container
            FormatSelector = FormatSelector,
            MergeOutputFormat = MergeOutputFormat,
            EmbedMetadata = EmbedMetadata,
            EmbedThumbnail = EmbedThumbnail,
            EmbedChapters = EmbedChapters,
            WriteDescription = WriteDescription,
            WriteInfoJson = WriteInfoJson,

            // Subtitles
            WriteSubs = WriteSubs,
            WriteAutoSubs = WriteAutoSubs,
            EmbedSubs = EmbedSubs,
            SubLangs = SubLangs,
            ConvertSubsTo = ConvertSubsTo,

            // Audio
            ExtractAudio = ExtractAudio,
            AudioCodec = ExtractAudio ? AudioCodec : string.Empty,
            AudioQuality = ExtractAudio ? AudioQuality : string.Empty,
            KeepVideo = KeepVideo,

            // Network
            CookiesFromBrowser = CookiesFromBrowser,
            CookiesBrowserProfile = CookiesBrowserProfile,
            CookiesFile = CookiesFile,
            Proxy = Proxy,
            RateLimit = RateLimit,
            ConcurrentFragments = ConcurrentFragments,
            Retries = Retries,

            // Playlist
            DownloadPlaylist = DownloadPlaylist,
            PlaylistItems = PlaylistItems,
            PlaylistReverse = PlaylistReverse,
            PlaylistRandom = PlaylistRandom,

            // Misc
            ForceOverwrites = ForceOverwrites,
            ContinuePartial = ContinuePartial,
            Verbose = Verbose,
            ExtraArgs = ExtraArgs,
        };
    }

    /// <summary>
    /// Bulk-overwrite the panel state (used by Conf import). Empty fields on
    /// <paramref name="o"/> preserve the existing value so an import that only
    /// covers a subset doesn't wipe what was already configured.
    /// </summary>
    public void ApplyOptions(DownloadOptions o)
    {
        // Output
        if (!string.IsNullOrEmpty(o.OutputFolder)) OutputFolder = o.OutputFolder;
        if (!string.IsNullOrEmpty(o.OutputTemplate)) OutputTemplate = o.OutputTemplate;
        NfcNormalize = o.NfcNormalize;
        RestrictFilenames = o.RestrictFilenames;
        WindowsFilenames = o.WindowsFilenames;

        // Format / container
        if (!string.IsNullOrEmpty(o.FormatSelector)) FormatSelector = o.FormatSelector;
        MergeOutputFormat = o.MergeOutputFormat;
        EmbedMetadata = o.EmbedMetadata;
        EmbedThumbnail = o.EmbedThumbnail;
        EmbedChapters = o.EmbedChapters;
        WriteDescription = o.WriteDescription;
        WriteInfoJson = o.WriteInfoJson;

        // Subtitles
        WriteSubs = o.WriteSubs;
        WriteAutoSubs = o.WriteAutoSubs;
        EmbedSubs = o.EmbedSubs;
        if (!string.IsNullOrEmpty(o.SubLangs)) SubLangs = o.SubLangs;
        if (!string.IsNullOrEmpty(o.ConvertSubsTo)) ConvertSubsTo = o.ConvertSubsTo;

        // Audio
        ExtractAudio = o.ExtractAudio;
        if (!string.IsNullOrEmpty(o.AudioCodec)) AudioCodec = o.AudioCodec;
        if (!string.IsNullOrEmpty(o.AudioQuality)) AudioQuality = o.AudioQuality;
        KeepVideo = o.KeepVideo;

        // Network
        if (!string.IsNullOrEmpty(o.CookiesFromBrowser)) CookiesFromBrowser = o.CookiesFromBrowser;
        if (!string.IsNullOrEmpty(o.CookiesBrowserProfile)) CookiesBrowserProfile = o.CookiesBrowserProfile;
        if (!string.IsNullOrEmpty(o.CookiesFile)) CookiesFile = o.CookiesFile;
        if (!string.IsNullOrEmpty(o.Proxy)) Proxy = o.Proxy;
        if (!string.IsNullOrEmpty(o.RateLimit)) RateLimit = o.RateLimit;
        if (o.ConcurrentFragments is int cf) ConcurrentFragments = cf;
        if (o.Retries is int r) Retries = r;

        // Playlist
        if (o.DownloadPlaylist is bool dp) DownloadPlaylist = dp;
        if (!string.IsNullOrEmpty(o.PlaylistItems)) PlaylistItems = o.PlaylistItems;
        PlaylistReverse = o.PlaylistReverse;
        PlaylistRandom = o.PlaylistRandom;

        // Misc
        ForceOverwrites = o.ForceOverwrites;
        ContinuePartial = o.ContinuePartial;
        Verbose = o.Verbose;
        if (!string.IsNullOrEmpty(o.ExtraArgs)) ExtraArgs = o.ExtraArgs;
    }

    // --- Browse helpers (used by the panel's small ⋯ buttons) ---

    [RelayCommand]
    private void BrowseCookiesFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select cookies file",
            Filter = "Netscape cookies (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (!string.IsNullOrWhiteSpace(CookiesFile))
        {
            var dir = Path.GetDirectoryName(CookiesFile);
            if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
            dlg.FileName = CookiesFile;
        }
        if (dlg.ShowDialog() == true) CookiesFile = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Output folder",
            InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : string.Empty,
        };
        if (dlg.ShowDialog() == true) OutputFolder = dlg.FolderName;
    }

    // --- Profile commands ---

    private bool CanSaveOrDeleteCurrent() => CurrentProfile is { IsBuiltIn: false };

    [RelayCommand(CanExecute = nameof(CanSaveOrDeleteCurrent))]
    private void SaveProfile()
    {
        if (CurrentProfile is not { IsBuiltIn: false } cur) return;
        _profiles.Upsert(new Profile { Name = cur.Name, Options = Snapshot() });
        CurrentProfile = Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, cur.Name, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void SaveAsProfile()
    {
        var owner = Application.Current?.MainWindow;
        var initial = CurrentProfile?.Name ?? string.Empty;
        var name = InputDialog.Prompt(owner, "Save profile as…", "Profile name:", initial);
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_profiles.ExistsUserProfile(name))
        {
            var ok = MessageBox.Show(owner,
                $"Overwrite the existing profile '{name}'?",
                "Save profile",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ok != MessageBoxResult.Yes) return;
        }

        _profiles.Upsert(new Profile { Name = name, Options = Snapshot() });
        CurrentProfile = Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(CanSaveOrDeleteCurrent))]
    private void DeleteProfile()
    {
        if (CurrentProfile is not { IsBuiltIn: false } cur) return;
        var owner = Application.Current?.MainWindow;
        var ok = MessageBox.Show(owner,
            $"Delete profile '{cur.Name}'? This cannot be undone.",
            "Delete profile",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;
        _profiles.Delete(cur.Name);
        CurrentProfile = null;
    }

    partial void OnCurrentProfileChanged(Profile? value)
    {
        SaveProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        if (_suppressProfileApply || value is null) return;
        ApplyProfile(value);
    }

    private void ApplyProfile(Profile p)
    {
        var po = p.Options;
        // Built-ins leave OutputFolder / OutputTemplate empty; preserve the
        // user's per-session values in that case.
        if (!string.IsNullOrEmpty(po.OutputFolder)) OutputFolder = po.OutputFolder;
        if (!string.IsNullOrEmpty(po.OutputTemplate)) OutputTemplate = po.OutputTemplate;
        FormatSelector = po.FormatSelector;
        MergeOutputFormat = po.MergeOutputFormat;
        EmbedMetadata = po.EmbedMetadata;
        EmbedThumbnail = po.EmbedThumbnail;
        EmbedChapters = po.EmbedChapters;
        ExtractAudio = po.ExtractAudio;
        if (!string.IsNullOrEmpty(po.AudioCodec)) AudioCodec = po.AudioCodec;
        if (!string.IsNullOrEmpty(po.AudioQuality)) AudioQuality = po.AudioQuality;
    }

    private void RefreshProfiles()
    {
        var selectedName = CurrentProfile?.Name;
        Profiles.Clear();
        foreach (var p in _profiles.All) Profiles.Add(p);
        _suppressProfileApply = true;
        try
        {
            CurrentProfile = selectedName is null
                ? null
                : Profiles.FirstOrDefault(p => string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        }
        finally { _suppressProfileApply = false; }
    }
}

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtDlpGui.Models;
using YtDlpGui.Services;
using YtDlpGui.Views;

namespace YtDlpGui.ViewModels;

/// <summary>
/// View-model for the Options panel. Wraps the subset of <see cref="DownloadOptions"/>
/// that the panel exposes; the rest stay at their model defaults until later
/// versions add UI for them.
///
/// On construction, restores from <see cref="AppSettings.LastOptions"/> if present,
/// else seeds from <see cref="AppSettings.DefaultOutputFolder"/> (or ~/Downloads).
/// Owns the Profile picker (SPEC §5.2 mode 1) and delegates to <see cref="IProfileStore"/>
/// for built-ins and user-saved presets.
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
        if (saved is not null)
        {
            // Restore last-committed Options panel state (v0.3 single-profile stand-in).
            OutputFolder = string.IsNullOrEmpty(saved.OutputFolder) ? FallbackFolder(settings) : saved.OutputFolder;
            OutputTemplate = string.IsNullOrEmpty(saved.OutputTemplate) ? "%(title)s.%(ext)s" : saved.OutputTemplate;
            NfcNormalize = saved.NfcNormalize;
            FormatSelector = string.IsNullOrEmpty(saved.FormatSelector) ? _formatSelector : saved.FormatSelector;
            MergeOutputFormat = saved.MergeOutputFormat;
            EmbedMetadata = saved.EmbedMetadata;
            EmbedThumbnail = saved.EmbedThumbnail;
            EmbedChapters = saved.EmbedChapters;
            ExtractAudio = saved.ExtractAudio;
            if (!string.IsNullOrEmpty(saved.AudioCodec)) AudioCodec = saved.AudioCodec;
            if (!string.IsNullOrEmpty(saved.AudioQuality)) AudioQuality = saved.AudioQuality;
            Verbose = saved.Verbose;
            ExtraArgs = saved.ExtraArgs;
        }
        else
        {
            OutputFolder = FallbackFolder(settings);
        }
    }

    private static string FallbackFolder(ISettingsStore settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Current.DefaultOutputFolder))
            return settings.Current.DefaultOutputFolder;
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

    // --- Format ---
    [ObservableProperty] private string _formatSelector =
        "bv*[ext=mp4][vcodec^=avc]+ba[ext=m4a]/bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/b";

    /// <summary>"mp4" | "mkv" | "webm" | "" (auto).</summary>
    [ObservableProperty] private string _mergeOutputFormat = "mp4";

    // --- Embed ---
    [ObservableProperty] private bool _embedMetadata = true;
    [ObservableProperty] private bool _embedThumbnail = true;
    [ObservableProperty] private bool _embedChapters;

    // --- Audio extraction (toggle group) ---
    [ObservableProperty] private bool _extractAudio;
    [ObservableProperty] private string _audioCodec = "mp3";
    [ObservableProperty] private string _audioQuality = "0";

    // --- Misc ---
    [ObservableProperty] private bool _verbose;
    [ObservableProperty] private string _extraArgs = string.Empty;

    /// <summary>Produce a snapshot for enqueueing — mutations to this VM after enqueue won't affect the in-flight item.</summary>
    public DownloadOptions Snapshot()
    {
        return new DownloadOptions
        {
            OutputFolder = OutputFolder,
            OutputTemplate = OutputTemplate,
            NfcNormalize = NfcNormalize,
            FormatSelector = FormatSelector,
            MergeOutputFormat = MergeOutputFormat,
            EmbedMetadata = EmbedMetadata,
            EmbedThumbnail = EmbedThumbnail,
            EmbedChapters = EmbedChapters,
            ExtractAudio = ExtractAudio,
            AudioCodec = ExtractAudio ? AudioCodec : string.Empty,
            AudioQuality = ExtractAudio ? AudioQuality : string.Empty,
            Verbose = Verbose,
            ExtraArgs = ExtraArgs,
        };
    }

    /// <summary>
    /// Bulk-overwrite the panel state (used by Conf import). Equivalent to
    /// <see cref="ApplyProfile"/> but without the empty-field preservation —
    /// the importer always passes a fully-formed options object.
    /// </summary>
    public void ApplyOptions(DownloadOptions o)
    {
        OutputFolder = string.IsNullOrEmpty(o.OutputFolder) ? OutputFolder : o.OutputFolder;
        OutputTemplate = string.IsNullOrEmpty(o.OutputTemplate) ? OutputTemplate : o.OutputTemplate;
        NfcNormalize = o.NfcNormalize;
        FormatSelector = string.IsNullOrEmpty(o.FormatSelector) ? FormatSelector : o.FormatSelector;
        MergeOutputFormat = o.MergeOutputFormat;
        EmbedMetadata = o.EmbedMetadata;
        EmbedThumbnail = o.EmbedThumbnail;
        EmbedChapters = o.EmbedChapters;
        ExtractAudio = o.ExtractAudio;
        if (!string.IsNullOrEmpty(o.AudioCodec)) AudioCodec = o.AudioCodec;
        if (!string.IsNullOrEmpty(o.AudioQuality)) AudioQuality = o.AudioQuality;
        Verbose = o.Verbose;
        if (!string.IsNullOrEmpty(o.ExtraArgs)) ExtraArgs = o.ExtraArgs;
    }

    // --- Profile commands ---

    private bool CanSaveOrDeleteCurrent() => CurrentProfile is { IsBuiltIn: false };

    [RelayCommand(CanExecute = nameof(CanSaveOrDeleteCurrent))]
    private void SaveProfile()
    {
        if (CurrentProfile is not { IsBuiltIn: false } cur) return;
        _profiles.Upsert(new Profile { Name = cur.Name, Options = Snapshot() });
        // Re-select by name after the Changed event refreshes the collection.
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
        // Don't re-apply when the change is the post-save Refresh swapping in a
        // new instance of the same profile — that would clobber the just-saved
        // values until the explicit re-select re-applied them.
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
            // Re-select by name. If gone (deleted), fall through to null.
            CurrentProfile = selectedName is null
                ? null
                : Profiles.FirstOrDefault(p => string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        }
        finally { _suppressProfileApply = false; }
    }
}

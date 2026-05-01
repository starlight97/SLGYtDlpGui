using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using YtDlpGui.Models;
using YtDlpGui.Services;

namespace YtDlpGui.ViewModels;

/// <summary>
/// View-model for the basic Options panel. Wraps the subset of
/// <see cref="DownloadOptions"/> that the panel exposes; the rest stay at their
/// model defaults until later versions add UI for them.
///
/// On construction, restores from <see cref="AppSettings.LastOptions"/> if present,
/// else seeds from <see cref="AppSettings.DefaultOutputFolder"/> (or ~/Downloads).
/// </summary>
public sealed partial class OptionsViewModel : ObservableObject
{
    public OptionsViewModel(ISettingsStore settings)
    {
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
}

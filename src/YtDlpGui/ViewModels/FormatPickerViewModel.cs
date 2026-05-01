using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtDlpGui.Models;
using YtDlpGui.Services;

namespace YtDlpGui.ViewModels;

/// <summary>
/// SPEC §5.4: format inspector. Drives one run of <c>yt-dlp -F --dump-single-json</c>,
/// presents the parsed formats, and composes a <c>-f</c> selector from the user's
/// video / audio picks.
/// </summary>
public sealed partial class FormatPickerViewModel : ObservableObject
{
    private readonly IFormatInspector _inspector;
    private CancellationTokenSource? _cts;

    public FormatPickerViewModel(IFormatInspector inspector)
    {
        _inspector = inspector;
    }

    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private bool _isInspecting;
    [ObservableProperty] private string _statusText = "Paste a URL and press Inspect.";
    [ObservableProperty] private string? _videoTitle;
    [ObservableProperty] private string _composedSelector = string.Empty;

    [ObservableProperty] private FormatInfo? _selectedRow;
    [ObservableProperty] private FormatInfo? _selectedVideo;
    [ObservableProperty] private FormatInfo? _selectedAudio;

    public ObservableCollection<FormatInfo> Formats { get; } = new();

    /// <summary>
    /// Set to a non-null selector when the user clicks Apply. The host window
    /// reads this on close to push the value back into the Options panel.
    /// </summary>
    public string? Result { get; private set; }
    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    private bool CanRun() => !IsInspecting && !string.IsNullOrWhiteSpace(Url);
    private bool CanCancel() => IsInspecting;
    private bool HaveFormats() => Formats.Count > 0;
    private bool HaveSelectedRow() => SelectedRow is not null;
    private bool CanApply() => !string.IsNullOrEmpty(ComposedSelector);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunInspectAsync()
    {
        var url = Url.Trim();
        if (url.Length == 0) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsInspecting = true;
        StatusText = "Inspecting…";
        Formats.Clear();
        VideoTitle = null;
        SelectedVideo = null;
        SelectedAudio = null;
        UpdateComposed();
        NotifyCommands();

        try
        {
            var r = await _inspector.InspectAsync(url, _cts.Token).ConfigureAwait(true);
            VideoTitle = r.Title;
            foreach (var f in r.Formats) Formats.Add(f);
            StatusText = r.Formats.Count == 0
                ? "No formats reported."
                : $"{r.Formats.Count} formats. Pick a video row + Set as Video, an audio row + Set as Audio (or use the Best buttons).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Canceled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
        }
        finally
        {
            IsInspecting = false;
            NotifyCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelInspect() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(HaveSelectedRow))]
    private void SetAsVideo()
    {
        SelectedVideo = SelectedRow;
        UpdateComposed();
    }

    [RelayCommand(CanExecute = nameof(HaveSelectedRow))]
    private void SetAsAudio()
    {
        SelectedAudio = SelectedRow;
        UpdateComposed();
    }

    [RelayCommand]
    private void ClearVideo() { SelectedVideo = null; UpdateComposed(); }

    [RelayCommand]
    private void ClearAudio() { SelectedAudio = null; UpdateComposed(); }

    [RelayCommand(CanExecute = nameof(HaveFormats))]
    private void PickBestVideo()
    {
        // bv* style: top video-only by tbr (fall back to combined formats sorted by tbr).
        SelectedVideo =
            Formats.Where(f => f.IsVideoOnly).OrderByDescending(f => f.Tbr ?? 0).FirstOrDefault()
            ?? Formats.Where(f => f.IsCombined).OrderByDescending(f => f.Tbr ?? 0).FirstOrDefault();
        UpdateComposed();
    }

    [RelayCommand(CanExecute = nameof(HaveFormats))]
    private void PickBestAudio()
    {
        // ba style: top audio-only by tbr.
        SelectedAudio =
            Formats.Where(f => f.IsAudioOnly).OrderByDescending(f => f.Tbr ?? 0).FirstOrDefault();
        UpdateComposed();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        Result = ComposedSelector;
        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        DialogResult = false;
        _cts?.Cancel();
        RequestClose?.Invoke();
    }

    partial void OnUrlChanged(string value) => RunInspectCommand.NotifyCanExecuteChanged();
    partial void OnIsInspectingChanged(bool value) => NotifyCommands();
    partial void OnSelectedRowChanged(FormatInfo? value) => NotifyCommands();
    partial void OnSelectedVideoChanged(FormatInfo? value) => UpdateComposed();
    partial void OnSelectedAudioChanged(FormatInfo? value) => UpdateComposed();
    partial void OnComposedSelectorChanged(string value) => ApplyCommand.NotifyCanExecuteChanged();

    private void UpdateComposed()
    {
        ComposedSelector = (SelectedVideo, SelectedAudio) switch
        {
            ({ Id: var v }, { Id: var a }) => $"{v}+{a}",
            ({ Id: var v }, null) => v,
            (null, { Id: var a }) => a,
            _ => string.Empty,
        };
    }

    private void NotifyCommands()
    {
        RunInspectCommand.NotifyCanExecuteChanged();
        CancelInspectCommand.NotifyCanExecuteChanged();
        SetAsVideoCommand.NotifyCanExecuteChanged();
        SetAsAudioCommand.NotifyCanExecuteChanged();
        PickBestVideoCommand.NotifyCanExecuteChanged();
        PickBestAudioCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
    }
}

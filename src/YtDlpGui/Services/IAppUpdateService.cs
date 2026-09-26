namespace YtDlpGui.Services;

public enum AppUpdateState
{
    /// <summary>Not a Velopack install (F5 / unzipped build): every operation is a no-op.</summary>
    NotInstalled,
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    /// <summary>Downloaded; applied by "Restart to update" or automatically on the next launch.</summary>
    ReadyToRestart,
    Failed,
}

/// <summary>Immutable snapshot, replaced wholesale on every change so any thread sees a consistent view.</summary>
public sealed record AppUpdateStatus(
    AppUpdateState State,
    string CurrentVersion,
    string? AvailableVersion = null,
    int Progress = 0,
    string? Error = null);

/// <summary>App self-update via Velopack + GitHub Releases (separate from yt-dlp's own -U).</summary>
public interface IAppUpdateService
{
    bool IsInstalled { get; }
    AppUpdateStatus Status { get; }
    bool IsApplyScheduled { get; }

    /// <summary>Raised on an arbitrary thread; marshal to the UI thread before touching bound properties.</summary>
    event Action<AppUpdateStatus>? StatusChanged;

    /// <summary>Never throws (failures → Failed + log). Returns the current status if a Velopack call is already running.</summary>
    Task<AppUpdateStatus> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads the release found by the last CheckAsync. Never throws.</summary>
    Task<AppUpdateStatus> DownloadAsync(CancellationToken ct = default);

    /// <summary>
    /// Arms the hand-off Program.Main runs after the WPF app exits. False unless ReadyToRestart.
    /// Raises <see cref="StatusChanged"/> on success so CanExecute predicates gated on
    /// <see cref="IsApplyScheduled"/> (in either VM, not just the caller) requery.
    /// </summary>
    bool ScheduleApplyOnExit();

    /// <summary>Reverts <see cref="ScheduleApplyOnExit"/>. Raises <see cref="StatusChanged"/> if something
    /// was actually scheduled; a no-op call (nothing scheduled) does not raise.</summary>
    void CancelScheduledApply();

    /// <summary>True if another process runs this same exe — applying an update would force-kill it.</summary>
    bool IsAnotherInstanceRunning();
}

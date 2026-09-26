using YtDlpGui.Services;

namespace YtDlpGui.Tests;

/// <summary>
/// Always-NotInstalled, no-op <see cref="IAppUpdateService"/> by default. Used by ViewModel tests that
/// need the constructor parameter but don't exercise the app-update flow itself. <see cref="Status"/> and
/// <see cref="IsApplyScheduled"/> are settable, and <see cref="RaiseStatusChanged"/> drives the real
/// <see cref="StatusChanged"/> event, for tests that need to exercise the reverse-guard / requery wiring
/// (yt-dlp self-update must be blocked while an app update is downloading or a restart-to-apply is scheduled).
/// </summary>
internal sealed class FakeAppUpdateService : IAppUpdateService
{
    public bool IsInstalled { get; set; }
    public AppUpdateStatus Status { get; set; } = new(AppUpdateState.NotInstalled, "0.0.0");
    public bool IsApplyScheduled { get; set; }

    public event Action<AppUpdateStatus>? StatusChanged;

    public Task<AppUpdateStatus> CheckAsync(CancellationToken ct = default) => Task.FromResult(Status);
    public Task<AppUpdateStatus> DownloadAsync(CancellationToken ct = default) => Task.FromResult(Status);
    public bool ScheduleApplyOnExit() => false;
    public void CancelScheduledApply() { }
    public bool IsAnotherInstanceRunning() => false;

    /// <summary>Sets <see cref="Status"/> and raises <see cref="StatusChanged"/>, mimicking the real
    /// service's <c>Publish</c>.</summary>
    public void RaiseStatusChanged(AppUpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    /// <summary>Lets tests assert a consumer actually unsubscribed (e.g. a closed ViewModel) —
    /// <see cref="StatusChanged"/> itself isn't inspectable from outside the class.</summary>
    public bool HasStatusChangedSubscribers => StatusChanged is not null;
}

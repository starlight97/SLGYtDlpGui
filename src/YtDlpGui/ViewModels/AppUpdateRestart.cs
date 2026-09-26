using System.Windows;
using YtDlpGui.Services;

namespace YtDlpGui.ViewModels;

/// <summary>"Restart to update" plumbing shared by Settings (Phase A) and the main-window banner (Phase B).</summary>
internal static class AppUpdateRestart
{
    /// <summary>Warns about other instances (Velopack force-kills them), then arms the post-exit hand-off.</summary>
    public static bool ConfirmAndSchedule(IAppUpdateService updates, Window? owner)
    {
        if (updates.IsAnotherInstanceRunning())
        {
            var answer = MessageBox.Show(owner!,
                "Another YtDlpGui window is open. Installing the update closes it immediately — its downloads " +
                "are cancelled without asking. Continue?",
                "Restart to update", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return false;
        }
        return updates.ScheduleApplyOnExit();
    }

    /// <summary>
    /// Exits through the normal main-window close so MainWindow.OnClosing's prompts (yt-dlp -U, active downloads)
    /// and App.OnExit's settings save still run. Close() is synchronous: still visible = the user said "No".
    /// </summary>
    public static void CloseMainWindow(IAppUpdateService updates)
    {
        var main = Application.Current?.MainWindow;
        if (main is null) { Application.Current?.Shutdown(); return; }
        main.Close();
        if (main.IsVisible) updates.CancelScheduledApply();
    }
}

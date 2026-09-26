using System.IO;
using System.Windows;
using Serilog;
using Velopack;
using YtDlpGui.Services;

namespace YtDlpGui;

/// <summary>
/// Custom entry point (csproj StartupObject). VelopackApp must run before any WPF code: when
/// Setup/Update.exe launch the app with hook arguments (install/update/uninstall) Run() handles
/// them and exits. It also auto-applies a previously downloaded update on startup.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        try
        {
            // Applying an update force-kills every process started from the install folder (no Closing
            // prompt), so never auto-apply while another YtDlpGui window — maybe mid-download — is open.
            var anotherInstance = AppUpdateService.IsAnotherInstanceRunningCore();
            VelopackApp.Build()
                .SetAutoApplyOnStartup(!anotherInstance)
                .Run();

            LeaveAppFolder();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            // App.OnExit didn't run on this path, so flush whatever Serilog buffered.
            Log.Fatal(ex, "Unhandled exception");
            Log.CloseAndFlush();
            MessageBox.Show($"YtDlpGui crashed:\n{ex.Message}\n\nDetails are in the log folder.",
                "YtDlpGui", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // App.OnExit has saved settings and flushed logs; hand off to Update.exe if "Restart to update" was chosen.
        AppUpdateService.ApplyScheduledUpdateOnExit();
    }

    /// <summary>
    /// Shortcuts and Update.exe start us with CWD = ...\current, the folder Velopack replaces on every
    /// update. yt-dlp children started with an empty WorkingDirectory inherit it (blocking the swap,
    /// and resolving relative paths into a folder that gets wiped), so move to the user profile.
    /// </summary>
    private static void LeaveAppFolder()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(home)) return;
            var target = GetWorkingDirectoryOverride(Environment.CurrentDirectory, AppContext.BaseDirectory, home);
            if (target is not null) Environment.CurrentDirectory = target;
        }
        catch
        {
            // Keep the inherited directory; only relative paths and update swaps are affected.
        }
    }

    /// <summary>Returns <paramref name="home"/> when <paramref name="cwd"/> is the app folder or inside it; otherwise null.</summary>
    internal static string? GetWorkingDirectoryOverride(string cwd, string appDir, string home)
    {
        var app = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cwd));
        var inside = current.Equals(app, StringComparison.OrdinalIgnoreCase)
            || current.StartsWith(app + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return inside ? home : null;
    }
}

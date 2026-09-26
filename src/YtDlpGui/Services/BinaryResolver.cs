using System.Diagnostics;
using System.IO;

namespace YtDlpGui.Services;

public sealed class BinaryResolver : IBinaryResolver
{
    /// <summary>Sub-folder of the data dir (next to settings.json). Survives app updates, unlike the install folder.</summary>
    public const string ManagedBinFolderName = "bin";

    private readonly ISettingsStore _settings;

    public BinaryResolver(ISettingsStore settings)
    {
        _settings = settings;
    }

    /// <summary>%LocalAppData%\YtDlpGui\bin — derived from the settings file so the data root is defined once.</summary>
    public static string GetManagedBinDirectory(ISettingsStore settings)
    {
        var dataDir = Path.GetDirectoryName(settings.SettingsFilePath);
        return string.IsNullOrEmpty(dataDir) ? string.Empty : Path.Combine(dataDir, ManagedBinFolderName);
    }

    public string? ResolveYtDlp() => Resolve("yt-dlp.exe", _settings.Current.YtDlpPathOverride,
        GetManagedBinDirectory(_settings), AppContext.BaseDirectory);
    public string? ResolveFfmpeg() => Resolve("ffmpeg.exe", _settings.Current.FfmpegPathOverride,
        GetManagedBinDirectory(_settings), AppContext.BaseDirectory);

    /// <summary>Order (pinned by BinaryResolverTests): override → tools folder → next to exe → parent dirs (dev only) → PATH.</summary>
    internal static string? Resolve(string exeName, string? overridePath, string? managedBinDir, string baseDirectory)
    {
        // 0. Settings override wins if it points to a real file.
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;

        // 1. Update-safe tools folder. The installed app folder (...\current) is replaced on every update.
        if (!string.IsNullOrEmpty(managedBinDir))
        {
            var managed = Path.Combine(managedBinDir, exeName);
            if (File.Exists(managed)) return managed;
        }

        // 2. Same folder as the GUI exe (portable / dev builds).
        var sideBySide = Path.Combine(baseDirectory, exeName);
        if (File.Exists(sideBySide)) return sideBySide;

#if DEBUG
        // 3. Walk up to repo root (handy in dev: src/YtDlpGui/bin/... -> repo root has yt-dlp.exe).
        //    Dev only: an installed app's baseDirectory is ...\SLGYtDlpGui\current, and walking up from
        //    there would reach %LocalAppData% and the user's home folder, picking up stale yt-dlp.exe.
        var dir = new DirectoryInfo(baseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, exeName);
            if (File.Exists(candidate)) return candidate;
        }
#endif

        // 4. PATH lookup via `where`.
        try
        {
            var psi = new ProcessStartInfo("where.exe", exeName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            if (proc.ExitCode == 0)
            {
                var firstLine = stdout
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine)) return firstLine;
            }
        }
        catch
        {
            // ignored — fall through to null
        }

        return null;
    }
}

using System.Diagnostics;
using System.IO;

namespace YtDlpGui.Services;

public sealed class BinaryResolver : IBinaryResolver
{
    private readonly ISettingsStore _settings;

    public BinaryResolver(ISettingsStore settings)
    {
        _settings = settings;
    }

    public string? ResolveYtDlp() => Resolve("yt-dlp.exe", _settings.Current.YtDlpPathOverride);
    public string? ResolveFfmpeg() => Resolve("ffmpeg.exe", _settings.Current.FfmpegPathOverride);

    private static string? Resolve(string exeName, string? overridePath)
    {
        // 0. Settings override wins if it points to a real file.
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        // 1. Same folder as the GUI exe.
        var here = AppContext.BaseDirectory;
        var sideBySide = Path.Combine(here, exeName);
        if (File.Exists(sideBySide)) return sideBySide;

        // 2. Walk up to repo root (handy in dev: src/YtDlpGui/bin/... -> repo root has yt-dlp.exe).
        var dir = new DirectoryInfo(here);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, exeName);
            if (File.Exists(candidate)) return candidate;
        }

        // 3. PATH lookup via `where`.
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

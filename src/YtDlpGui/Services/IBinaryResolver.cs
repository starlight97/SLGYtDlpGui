namespace YtDlpGui.Services;

public interface IBinaryResolver
{
    /// <summary>Resolves the absolute path to yt-dlp.exe, or null if not found.</summary>
    string? ResolveYtDlp();

    /// <summary>Resolves the absolute path to ffmpeg.exe, or null if not found.</summary>
    string? ResolveFfmpeg();
}

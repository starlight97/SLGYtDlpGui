using System.IO;
using System.Text;

namespace YtDlpGui.Infrastructure.Hangul;

/// <summary>
/// NFC-normalizes filenames. yt-dlp on Windows produces Hangul filenames in NFD
/// form (decomposed jamo) which renders as "ㅇㅜㅅㅏㅁㄱㅕㅂ" instead of "우삼겹".
/// SPEC §8: try FormC first, fall back to FormKC.
/// </summary>
public static class NfcNormalizer
{
    /// <summary>
    /// Renames the file at <paramref name="filePath"/> to its NFC-normalized form.
    /// Returns the new path (or the original if already normalized / not found).
    /// </summary>
    public static string NormalizeInPlace(string filePath)
    {
        if (!File.Exists(filePath)) return filePath;

        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var name = Path.GetFileName(filePath);

        var normalized = name.Normalize(NormalizationForm.FormC);
        if (string.Equals(name, normalized, StringComparison.Ordinal))
            normalized = name.Normalize(NormalizationForm.FormKC);

        if (string.Equals(name, normalized, StringComparison.Ordinal))
            return filePath; // already normalized

        var newPath = Path.Combine(dir, normalized);

        if (File.Exists(newPath))
        {
            // An NFC-named file already exists; drop the NFD duplicate.
            File.Delete(filePath);
        }
        else
        {
            File.Move(filePath, newPath);
        }
        return newPath;
    }

    /// <summary>
    /// Walks <paramref name="folder"/> (non-recursive) and NFC-normalizes any file
    /// whose name differs from its FormC/FormKC normalization. Optionally restricted
    /// to files modified at or after <paramref name="modifiedSince"/>.
    /// Returns the count of files actually renamed.
    /// </summary>
    public static int NormalizeFolder(string folder, DateTime? modifiedSince = null, Action<string>? log = null)
    {
        if (!Directory.Exists(folder)) return 0;
        var renamed = 0;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (modifiedSince.HasValue)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < modifiedSince.Value) continue;
                }
                catch { continue; }
            }

            try
            {
                var before = Path.GetFileName(path);
                var newPath = NormalizeInPlace(path);
                var after = Path.GetFileName(newPath);
                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    log?.Invoke($"[gui] NFC: {before} -> {after}");
                    renamed++;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[gui] NFC normalize failed for {Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return renamed;
    }
}

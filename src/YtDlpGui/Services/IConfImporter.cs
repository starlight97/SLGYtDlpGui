using YtDlpGui.Models;

namespace YtDlpGui.Services;

public sealed record ConfImportResult(
    DownloadOptions Options,
    /// <summary>Flags successfully mapped onto <see cref="DownloadOptions"/>. Listed in source order.</summary>
    IReadOnlyList<string> AppliedFlags,
    /// <summary>Flags not in the GUI's mapping table — surfaced to the user in the import summary.</summary>
    IReadOnlyList<string> UnrecognizedFlags);

public interface IConfImporter
{
    /// <summary>Read and parse a <c>yt-dlp.conf</c>-style file from disk.</summary>
    ConfImportResult Import(string filePath);

    /// <summary>Parse text directly. Useful for tests and paste flows.</summary>
    ConfImportResult ImportText(string text);
}

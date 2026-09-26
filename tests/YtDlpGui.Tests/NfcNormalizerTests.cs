using System.IO;
using System.Text;
using Xunit;
using YtDlpGui.Infrastructure.Hangul;

namespace YtDlpGui.Tests;

public class NfcNormalizerTests : IDisposable
{
    private readonly string _tempDir;

    public NfcNormalizerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "YtDlpGuiTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void NormalizeInPlace_NfdHangul_RenamesToNfc()
    {
        var nfc = "우삼겹.mp4";
        var nfd = nfc.Normalize(NormalizationForm.FormD);
        // Sanity: the two forms must actually differ for this test to mean anything.
        Assert.NotEqual(nfc, nfd);

        var nfdPath = Path.Combine(_tempDir, nfd);
        File.WriteAllText(nfdPath, "test");

        var newPath = NfcNormalizer.NormalizeInPlace(nfdPath);

        Assert.Equal(Path.Combine(_tempDir, nfc), newPath);
        Assert.True(File.Exists(newPath));
    }

    [Fact]
    public void NormalizeInPlace_AlreadyNfc_NoOp()
    {
        var name = "already-nfc.mp4";
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "test");

        var newPath = NfcNormalizer.NormalizeInPlace(path);

        Assert.Equal(path, newPath);
        Assert.True(File.Exists(newPath));
    }

    [Fact]
    public void NormalizeInPlace_MissingFile_ReturnsOriginal()
    {
        var nonExistent = Path.Combine(_tempDir, "missing.txt");
        var newPath = NfcNormalizer.NormalizeInPlace(nonExistent);
        Assert.Equal(nonExistent, newPath);
    }

    [Fact]
    public void NormalizeFolder_RenamesAllNfdFiles()
    {
        var names = new[] { "우삼겹.mp4", "낙곱새.mp4", "abc.txt" };
        foreach (var n in names)
        {
            var nfd = n.Normalize(NormalizationForm.FormD);
            File.WriteAllText(Path.Combine(_tempDir, nfd), "x");
        }

        var renamed = NfcNormalizer.NormalizeFolder(_tempDir);
        Assert.Equal(2, renamed); // abc.txt is already NFC, so 2 hangul files renamed

        foreach (var n in names)
            Assert.True(File.Exists(Path.Combine(_tempDir, n)));
    }

    [Fact]
    public void NormalizeFolder_ModifiedSinceFiltersOldFiles()
    {
        // Old NFD file (modified an hour ago)
        var oldNfd = "오래된파일.mp4".Normalize(NormalizationForm.FormD);
        var oldPath = Path.Combine(_tempDir, oldNfd);
        File.WriteAllText(oldPath, "old");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddHours(-1));

        // Recent NFD file
        var newNfd = "새파일.mp4".Normalize(NormalizationForm.FormD);
        var newPath = Path.Combine(_tempDir, newNfd);
        File.WriteAllText(newPath, "new");

        var renamed = NfcNormalizer.NormalizeFolder(_tempDir, modifiedSince: DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(1, renamed);
        Assert.True(File.Exists(oldPath));   // outside the window — left alone
        Assert.False(File.Exists(newPath));  // renamed
        Assert.True(File.Exists(Path.Combine(_tempDir, "새파일.mp4".Normalize(NormalizationForm.FormC))));
    }
}

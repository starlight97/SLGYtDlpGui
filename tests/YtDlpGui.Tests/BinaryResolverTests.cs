using System.IO;
using Xunit;
using YtDlpGui.Models;
using YtDlpGui.Services;

namespace YtDlpGui.Tests;

/// <summary>
/// Pins <see cref="BinaryResolver"/>'s lookup order: override -> tools folder (survives app
/// updates, D7) -> next to the GUI exe -> parent dirs (dev only, see BinaryResolver.Resolve) -> PATH.
/// Uses a name unique enough (<see cref="FakeExeName"/>) that it can never collide with a real
/// yt-dlp/ffmpeg on PATH or a stray file in a parent folder, so the "not found" case is deterministic.
/// </summary>
public class BinaryResolverTests : IDisposable
{
    private const string FakeExeName = "ytdlpgui-resolver-test.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlpgui-resolver-tests-" + Guid.NewGuid());

    private string DataDir => Path.Combine(_root, "data");
    private string ManagedBinDir => Path.Combine(DataDir, BinaryResolver.ManagedBinFolderName);
    private string SideBySideDir => Path.Combine(_root, "app");
    private string SettingsFilePath => Path.Combine(DataDir, "settings.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static void CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    [Fact]
    public void GetManagedBinDirectory_IsDataDirSlashBin()
    {
        var settings = new FakeSettingsStore(SettingsFilePath);

        Assert.Equal(ManagedBinDir, BinaryResolver.GetManagedBinDirectory(settings));
    }

    [Fact]
    public void GetManagedBinDirectory_EmptySettingsPath_ReturnsEmpty()
    {
        var settings = new FakeSettingsStore(string.Empty);

        Assert.Equal(string.Empty, BinaryResolver.GetManagedBinDirectory(settings));
    }

    [Fact]
    public void Resolve_ToolsFolder_TakesPriorityOverSideBySide()
    {
        var managed = Path.Combine(ManagedBinDir, FakeExeName);
        var sideBySide = Path.Combine(SideBySideDir, FakeExeName);
        CreateFile(managed);
        CreateFile(sideBySide);

        var result = BinaryResolver.Resolve(FakeExeName, overridePath: null, ManagedBinDir, SideBySideDir);

        Assert.Equal(managed, result);
    }

    [Fact]
    public void Resolve_Override_TakesPriorityOverToolsFolder()
    {
        var overridePath = Path.Combine(_root, "override", FakeExeName);
        CreateFile(overridePath);
        CreateFile(Path.Combine(ManagedBinDir, FakeExeName));

        var result = BinaryResolver.Resolve(FakeExeName, overridePath, ManagedBinDir, SideBySideDir);

        Assert.Equal(overridePath, result);
    }

    [Fact]
    public void Resolve_OverrideMissing_FallsBackToToolsFolder()
    {
        var overridePath = Path.Combine(_root, "override", FakeExeName); // never created
        var managed = Path.Combine(ManagedBinDir, FakeExeName);
        CreateFile(managed);

        var result = BinaryResolver.Resolve(FakeExeName, overridePath, ManagedBinDir, SideBySideDir);

        Assert.Equal(managed, result);
    }

    [Fact]
    public void Resolve_ToolsFolderMissing_FallsBackToSideBySide()
    {
        var sideBySide = Path.Combine(SideBySideDir, FakeExeName);
        CreateFile(sideBySide);

        var result = BinaryResolver.Resolve(FakeExeName, overridePath: null, ManagedBinDir, SideBySideDir);

        Assert.Equal(sideBySide, result);
    }

    [Fact]
    public void Resolve_NotFoundAnywhere_ReturnsNull()
    {
        // ManagedBinDir/SideBySideDir intentionally left empty; FakeExeName is never on PATH either.
        var result = BinaryResolver.Resolve(FakeExeName, overridePath: null, ManagedBinDir, SideBySideDir);

        Assert.Null(result);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public FakeSettingsStore(string settingsFilePath) => SettingsFilePath = settingsFilePath;
        public AppSettings Current { get; } = new();
        public string SettingsFilePath { get; }
        public void Save() { }
    }
}

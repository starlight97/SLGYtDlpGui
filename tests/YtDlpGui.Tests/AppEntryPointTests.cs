using Xunit;
using YtDlpGui.Services;

namespace YtDlpGui.Tests;

/// <summary>
/// Verifies the csproj StartupObject switch (Program.cs) actually took effect, and pins
/// <see cref="Program.GetWorkingDirectoryOverride"/> (D11: leave the app folder as CWD so
/// yt-dlp children started with workingDirectory:null don't lock the Velopack "current" folder).
/// </summary>
public class AppEntryPointTests
{
    [Fact]
    public void EntryPoint_Is_ProgramMain()
    {
        var entryPoint = typeof(BinaryResolver).Assembly.EntryPoint;
        Assert.NotNull(entryPoint);
        Assert.Equal("YtDlpGui.Program", entryPoint!.DeclaringType?.FullName);
    }

    [Theory]
    [InlineData(@"C:\app", @"C:\app", @"C:\home")]
    [InlineData(@"C:\app\", @"C:\app", @"C:\home")]
    [InlineData(@"C:\app", @"C:\app\", @"C:\home")]
    [InlineData(@"C:\app\current", @"C:\app", @"C:\home")]
    [InlineData(@"C:\app\current\sub", @"C:\app", @"C:\home")]
    public void GetWorkingDirectoryOverride_InsideAppFolder_ReturnsHome(string cwd, string appDir, string home)
    {
        Assert.Equal(home, Program.GetWorkingDirectoryOverride(cwd, appDir, home));
    }

    [Theory]
    [InlineData(@"C:\other", @"C:\app")]
    [InlineData(@"C:\Users\someone", @"C:\app")]
    // Sibling folder whose name merely starts with the same prefix must not match.
    [InlineData(@"C:\app2", @"C:\app")]
    [InlineData(@"C:\app2\sub", @"C:\app")]
    public void GetWorkingDirectoryOverride_OutsideAppFolder_ReturnsNull(string cwd, string appDir)
    {
        Assert.Null(Program.GetWorkingDirectoryOverride(cwd, appDir, @"C:\home"));
    }
}

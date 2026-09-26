using System.Linq;
using Xunit;
using YtDlpGui.Infrastructure;
using YtDlpGui.Models;

namespace YtDlpGui.Tests;

public class ArgBuilderFfmpegLocationTests
{
    [Fact]
    public void Build_FfmpegPath_EmitsFfmpegLocation()
    {
        var args = ArgBuilder.Build(new DownloadOptions(), "https://x", @"C:\tools\ffmpeg.exe");
        var i = Array.IndexOf(args, "--ffmpeg-location");
        Assert.True(i >= 0);
        Assert.Equal(@"C:\tools\ffmpeg.exe", args[i + 1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Build_NoFfmpegPath_NotEmitted(string? ffmpegPath)
    {
        var args = ArgBuilder.Build(new DownloadOptions(), "https://x", ffmpegPath);
        Assert.DoesNotContain("--ffmpeg-location", args);
    }

    [Fact]
    public void Build_FfmpegPathWithSpaces_SingleArg()
    {
        var args = ArgBuilder.Build(new DownloadOptions(), "https://x", @"C:\Program Files\ffmpeg\bin\ffmpeg.exe");
        var i = Array.IndexOf(args, "--ffmpeg-location");
        Assert.True(i >= 0);
        Assert.Equal(@"C:\Program Files\ffmpeg\bin\ffmpeg.exe", args[i + 1]);
    }

    [Fact]
    public void Build_ExtraArgsHasFfmpegLocation_NotDuplicated()
    {
        var opts = new DownloadOptions { ExtraArgs = @"--ffmpeg-location D:\ff" };
        var args = ArgBuilder.Build(opts, "https://x", @"C:\tools\ffmpeg.exe");

        Assert.Equal(1, args.Count(a => a == "--ffmpeg-location"));
        var i = Array.IndexOf(args, "--ffmpeg-location");
        Assert.Equal(@"D:\ff", args[i + 1]);
    }

    [Fact]
    public void Build_ExtraArgsHasFfmpegLocationEqualsForm_NotDuplicated()
    {
        var opts = new DownloadOptions { ExtraArgs = @"--ffmpeg-location=D:\ff" };
        var args = ArgBuilder.Build(opts, "https://x", @"C:\tools\ffmpeg.exe");

        Assert.DoesNotContain("--ffmpeg-location", args);
        Assert.Contains(@"--ffmpeg-location=D:\ff", args);
    }

    [Fact]
    public void Build_WithFfmpegPath_UrlStillLast()
    {
        var args = ArgBuilder.Build(new DownloadOptions(), "https://example.com/v", @"C:\tools\ffmpeg.exe");
        Assert.Equal("https://example.com/v", args[^1]);
    }
}

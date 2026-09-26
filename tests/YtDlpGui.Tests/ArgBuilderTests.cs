using Xunit;
using YtDlpGui.Infrastructure;
using YtDlpGui.Models;

namespace YtDlpGui.Tests;

public class ArgBuilderTests
{
    [Fact]
    public void Build_AlwaysIncludesMandatoryPrefix()
    {
        // SPEC §4 / §7: every run carries the same always-on prefix.
        var args = ArgBuilder.Build(new DownloadOptions(), "https://x");
        Assert.Contains("--ignore-config", args);
        Assert.Contains("--no-color", args);
        Assert.Contains("--newline", args);
        Assert.Contains("--no-progress", args);
        Assert.Contains("--progress-template", args);
        Assert.Contains(ArgBuilder.ProgressTemplate, args);
    }

    [Fact]
    public void Build_AlwaysIncludesFinalFilePathPrint()
    {
        // The "Open file" command relies on yt-dlp emitting the resolved on-disk
        // path via --print after_move. If this gets dropped the Open button breaks.
        var args = ArgBuilder.Build(new DownloadOptions(), "https://x");
        var i = Array.IndexOf(args, "--print");
        Assert.True(i >= 0);
        var template = args[i + 1];
        Assert.StartsWith("after_move:", template);
        Assert.Contains(ArgBuilder.FilePathPrefix, template);
    }

    [Fact]
    public void Build_UrlIsLastArg()
    {
        var args = ArgBuilder.Build(new DownloadOptions(), "https://example.com/v");
        Assert.Equal("https://example.com/v", args[^1]);
    }

    [Fact]
    public void Build_EmptyOutputFolder_NotEmitted()
    {
        var opts = new DownloadOptions { OutputFolder = string.Empty };
        var args = ArgBuilder.Build(opts, "https://x");
        Assert.DoesNotContain("-P", args);
    }

    [Fact]
    public void Build_OutputFolderEmits_DashP()
    {
        var opts = new DownloadOptions { OutputFolder = @"D:\Downloads" };
        var args = ArgBuilder.Build(opts, "https://x");
        var i = Array.IndexOf(args, "-P");
        Assert.True(i >= 0);
        Assert.Equal(@"D:\Downloads", args[i + 1]);
    }

    [Fact]
    public void Build_FormatSelector_EmitsDashF()
    {
        var opts = new DownloadOptions { FormatSelector = "bv*+ba" };
        var args = ArgBuilder.Build(opts, "https://x");
        var i = Array.IndexOf(args, "-f");
        Assert.True(i >= 0);
        Assert.Equal("bv*+ba", args[i + 1]);
    }

    [Fact]
    public void Build_EmptyFormatSelector_NotEmitted()
    {
        var opts = new DownloadOptions { FormatSelector = string.Empty };
        var args = ArgBuilder.Build(opts, "https://x");
        Assert.DoesNotContain("-f", args);
    }

    [Fact]
    public void Build_ExtractAudio_EmitsXAndFormat()
    {
        var opts = new DownloadOptions
        {
            ExtractAudio = true,
            AudioCodec = "mp3",
            AudioQuality = "320",
        };
        var args = ArgBuilder.Build(opts, "https://x");
        Assert.Contains("-x", args);
        var formatIdx = Array.IndexOf(args, "--audio-format");
        Assert.True(formatIdx >= 0);
        Assert.Equal("mp3", args[formatIdx + 1]);
        var qualityIdx = Array.IndexOf(args, "--audio-quality");
        Assert.True(qualityIdx >= 0);
        Assert.Equal("320", args[qualityIdx + 1]);
    }

    [Fact]
    public void Build_ExtractAudioOff_DoesNotEmitX()
    {
        var args = ArgBuilder.Build(new DownloadOptions { ExtractAudio = false }, "https://x");
        Assert.DoesNotContain("-x", args);
        Assert.DoesNotContain("--audio-format", args);
    }

    [Fact]
    public void Build_KeepVideo_EmittedOnlyWithExtractAudio()
    {
        var withAudio = ArgBuilder.Build(
            new DownloadOptions { ExtractAudio = true, KeepVideo = true },
            "https://x");
        Assert.Contains("--keep-video", withAudio);
    }

    [Fact]
    public void Build_CookiesFromBrowserWithProfile_EmitsBrowserColonProfile()
    {
        var opts = new DownloadOptions
        {
            CookiesFromBrowser = "chrome",
            CookiesBrowserProfile = "Default",
        };
        var args = ArgBuilder.Build(opts, "https://x");
        var i = Array.IndexOf(args, "--cookies-from-browser");
        Assert.True(i >= 0);
        Assert.Equal("chrome:Default", args[i + 1]);
    }

    [Fact]
    public void Build_CookiesFromBrowserNoProfile_EmitsBrowserOnly()
    {
        var opts = new DownloadOptions { CookiesFromBrowser = "firefox" };
        var args = ArgBuilder.Build(opts, "https://x");
        var i = Array.IndexOf(args, "--cookies-from-browser");
        Assert.True(i >= 0);
        Assert.Equal("firefox", args[i + 1]);
    }

    [Fact]
    public void Build_DownloadPlaylistTrue_EmitsYes()
    {
        var args = ArgBuilder.Build(new DownloadOptions { DownloadPlaylist = true }, "https://x");
        Assert.Contains("--yes-playlist", args);
        Assert.DoesNotContain("--no-playlist", args);
    }

    [Fact]
    public void Build_DownloadPlaylistFalse_EmitsNo()
    {
        var args = ArgBuilder.Build(new DownloadOptions { DownloadPlaylist = false }, "https://x");
        Assert.Contains("--no-playlist", args);
        Assert.DoesNotContain("--yes-playlist", args);
    }

    [Fact]
    public void Build_DownloadPlaylistNull_NeitherEmitted()
    {
        var args = ArgBuilder.Build(new DownloadOptions { DownloadPlaylist = null }, "https://x");
        Assert.DoesNotContain("--yes-playlist", args);
        Assert.DoesNotContain("--no-playlist", args);
    }

    [Fact]
    public void Build_ExtraArgs_SplitOnWhitespace()
    {
        var opts = new DownloadOptions { ExtraArgs = "--foo bar  --baz qux" };
        var args = ArgBuilder.Build(opts, "https://x");
        Assert.Contains("--foo", args);
        Assert.Contains("bar", args);
        Assert.Contains("--baz", args);
        Assert.Contains("qux", args);
    }

    [Fact]
    public void Build_NfcNormalize_NotPassedToYtDlp()
    {
        // NfcNormalize is a GUI-side post-step; ArgBuilder must not emit a flag.
        var opts = new DownloadOptions { NfcNormalize = true };
        var args = ArgBuilder.Build(opts, "https://x");
        Assert.DoesNotContain("--nfc-normalize", args);
    }

    [Fact]
    public void FormatForLog_QuotesArgsWithSpaces()
    {
        var line = ArgBuilder.FormatForLog("yt-dlp.exe", new[] { "--paths", @"C:\Users\My Name\dl" });
        Assert.Contains(@"""C:\Users\My Name\dl""", line);
    }

    [Fact]
    public void FormatForLog_LeavesSimpleArgsUnquoted()
    {
        var line = ArgBuilder.FormatForLog("yt-dlp.exe", new[] { "-f", "bv*+ba" });
        Assert.Contains(" -f bv*+ba", line);
    }

    [Fact]
    public void Build_Subtitles_FullSet()
    {
        var opts = new DownloadOptions
        {
            WriteSubs = true,
            WriteAutoSubs = true,
            EmbedSubs = true,
            SubLangs = "ko,en",
            ConvertSubsTo = "srt",
        };
        var args = ArgBuilder.Build(opts, "https://x");
        Assert.Contains("--write-subs", args);
        Assert.Contains("--write-auto-subs", args);
        Assert.Contains("--embed-subs", args);
        var langsIdx = Array.IndexOf(args, "--sub-langs");
        Assert.Equal("ko,en", args[langsIdx + 1]);
        var convertIdx = Array.IndexOf(args, "--convert-subs");
        Assert.Equal("srt", args[convertIdx + 1]);
    }
}

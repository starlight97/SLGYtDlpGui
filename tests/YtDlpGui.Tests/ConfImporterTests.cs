using Xunit;
using YtDlpGui.Services;

namespace YtDlpGui.Tests;

public class ConfImporterTests
{
    private static ConfImportResult Parse(string text) => new ConfImporter().ImportText(text);

    [Fact]
    public void ImportText_FormatSelector()
    {
        var r = Parse("-f bestvideo+bestaudio");
        Assert.Equal("bestvideo+bestaudio", r.Options.FormatSelector);
        Assert.Contains("-f bestvideo+bestaudio", r.AppliedFlags);
    }

    [Fact]
    public void ImportText_OutputTemplate_EqualSyntax()
    {
        var r = Parse("--output=%(title)s.%(ext)s");
        Assert.Equal("%(title)s.%(ext)s", r.Options.OutputTemplate);
    }

    [Fact]
    public void ImportText_EmbedThumbnail_Boolean()
    {
        var r = Parse("--embed-thumbnail");
        Assert.True(r.Options.EmbedThumbnail);
    }

    [Fact]
    public void ImportText_NoEmbedThumbnail_Inverts()
    {
        var r = Parse("--no-embed-thumbnail");
        Assert.False(r.Options.EmbedThumbnail);
    }

    [Fact]
    public void ImportText_AddMetadata_AliasesEmbedMetadata()
    {
        var r = Parse("--add-metadata");
        Assert.True(r.Options.EmbedMetadata);
    }

    [Fact]
    public void ImportText_StripsLineComments()
    {
        var input = "# this is a comment\n-f bv*+ba\n# another\n--embed-thumbnail";
        var r = Parse(input);
        Assert.Equal("bv*+ba", r.Options.FormatSelector);
        Assert.True(r.Options.EmbedThumbnail);
    }

    [Fact]
    public void ImportText_TrailingHashIsComment()
    {
        var r = Parse("-f mp4 # default format");
        Assert.Equal("mp4", r.Options.FormatSelector);
    }

    [Fact]
    public void ImportText_QuotedArgWithSpaces()
    {
        var r = Parse("--output \"%(uploader)s/%(title)s.%(ext)s\"");
        Assert.Equal("%(uploader)s/%(title)s.%(ext)s", r.Options.OutputTemplate);
    }

    [Fact]
    public void ImportText_SingleQuotedArg()
    {
        var r = Parse("--output '%(title)s.%(ext)s'");
        Assert.Equal("%(title)s.%(ext)s", r.Options.OutputTemplate);
    }

    [Fact]
    public void ImportText_UnknownFlag_GoesToUnrecognized()
    {
        var r = Parse("--frobnicate xyzzy");
        Assert.Contains("--frobnicate xyzzy", r.UnrecognizedFlags);
    }

    [Fact]
    public void ImportText_UnknownBooleanFlag_NoValueConsumed()
    {
        // "--mumble" has no value; "-f mp4" should still apply.
        var r = Parse("--mumble\n-f mp4");
        Assert.Equal("mp4", r.Options.FormatSelector);
        Assert.Contains("--mumble", r.UnrecognizedFlags);
    }

    [Fact]
    public void ImportText_AlwaysOnFlags_SilentlyDropped()
    {
        var r = Parse("--ignore-config\n--no-color\n--newline\n--no-progress");
        Assert.Empty(r.AppliedFlags);
        Assert.Empty(r.UnrecognizedFlags);
    }

    [Fact]
    public void ImportText_CookiesBrowser_ParsesProfile()
    {
        var r = Parse("--cookies-from-browser chrome:Default");
        Assert.Equal("chrome", r.Options.CookiesFromBrowser);
        Assert.Equal("Default", r.Options.CookiesBrowserProfile);
    }

    [Fact]
    public void ImportText_CookiesBrowser_NoProfile()
    {
        var r = Parse("--cookies-from-browser firefox");
        Assert.Equal("firefox", r.Options.CookiesFromBrowser);
        Assert.Equal(string.Empty, r.Options.CookiesBrowserProfile);
    }

    [Fact]
    public void ImportText_RetriesParseInt()
    {
        var r = Parse("--retries 5");
        Assert.Equal(5, r.Options.Retries);
    }

    [Fact]
    public void ImportText_PlaylistTriState_Yes()
    {
        var r = Parse("--yes-playlist");
        Assert.True(r.Options.DownloadPlaylist);
    }

    [Fact]
    public void ImportText_PlaylistTriState_No()
    {
        var r = Parse("--no-playlist");
        Assert.False(r.Options.DownloadPlaylist);
    }

    [Fact]
    public void ImportText_ExtractAudio_FullSuite()
    {
        var r = Parse("-x --audio-format mp3 --audio-quality 320");
        Assert.True(r.Options.ExtractAudio);
        Assert.Equal("mp3", r.Options.AudioCodec);
        Assert.Equal("320", r.Options.AudioQuality);
    }

    [Fact]
    public void ImportText_EmptyInput_ReturnsEmptyResult()
    {
        var r = Parse(string.Empty);
        Assert.Empty(r.AppliedFlags);
        Assert.Empty(r.UnrecognizedFlags);
    }

    [Fact]
    public void ImportText_CommentOnlyInput_ReturnsEmptyResult()
    {
        var r = Parse("# just a comment\n# and another\n");
        Assert.Empty(r.AppliedFlags);
        Assert.Empty(r.UnrecognizedFlags);
    }

    [Fact]
    public void ImportText_PathsTypePrefix_StrippedForOutputFolder()
    {
        // POSIX shlex eats unquoted backslashes (matches yt-dlp's _read_user_conf), so
        // Windows paths in conf files must be quoted. Single-quoted preserves verbatim.
        var r = Parse(@"--paths home:'D:\Downloads'");
        Assert.Equal(@"D:\Downloads", r.Options.OutputFolder);
    }

    [Fact]
    public void ImportText_UnquotedBackslash_IsEatenByShlex()
    {
        // Documents the known limitation: an unquoted Windows path loses its backslashes.
        // Real-world conf files should quote the path or use forward slashes.
        var r = Parse(@"--paths D:\Downloads");
        Assert.Equal("D:Downloads", r.Options.OutputFolder);
    }

    [Fact]
    public void ImportText_SubLangs()
    {
        var r = Parse("--sub-langs ko,en,en.*");
        Assert.Equal("ko,en,en.*", r.Options.SubLangs);
    }
}

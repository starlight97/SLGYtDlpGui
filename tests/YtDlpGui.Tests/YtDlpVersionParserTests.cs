using Xunit;
using YtDlpGui.Infrastructure;

namespace YtDlpGui.Tests;

public class YtDlpVersionParserTests
{
    [Fact]
    public void TryParse_Stable()
    {
        var ok = YtDlpVersionParser.TryParse("2026.03.17", out var version, out var date);
        Assert.True(ok);
        Assert.Equal("2026.03.17", version);
        Assert.Equal(new DateOnly(2026, 3, 17), date);
    }

    [Fact]
    public void TryParse_Nightly_KeepsSuffixInVersion()
    {
        var ok = YtDlpVersionParser.TryParse("2026.08.19.232323", out var version, out var date);
        Assert.True(ok);
        Assert.Equal("2026.08.19.232323", version);
        Assert.Equal(new DateOnly(2026, 8, 19), date);
    }

    [Fact]
    public void TryParse_TrimsWhitespaceAndCrLf()
    {
        var ok = YtDlpVersionParser.TryParse("  2026.03.17\r\n", out var version, out var date);
        Assert.True(ok);
        Assert.Equal("2026.03.17", version);
        Assert.Equal(new DateOnly(2026, 3, 17), date);
    }

    [Fact]
    public void TryParse_SkipsNoiseLines()
    {
        var ok = YtDlpVersionParser.TryParse("WARNING: x\n2026.03.17\n", out var version, out var date);
        Assert.True(ok);
        Assert.Equal("2026.03.17", version);
        Assert.Equal(new DateOnly(2026, 3, 17), date);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yt-dlp")]
    [InlineData("2026.3.17")]
    [InlineData("2026.13.01")]
    [InlineData("2026.02.30")]
    [InlineData("v2026.03.17")]
    [InlineData("stable@2026.03.17")]
    [InlineData("2026.03.17 extra")]
    public void TryParse_Garbage_ReturnsFalse(string? output)
    {
        Assert.False(YtDlpVersionParser.TryParse(output, out _, out _));
    }

    [Fact]
    public void AgeInDays_SpecExample()
    {
        var age = YtDlpVersionParser.AgeInDays(new DateOnly(2026, 3, 17), new DateOnly(2026, 9, 26));
        Assert.Equal(193, age);
    }

    [Theory]
    [InlineData(90, false)]
    [InlineData(91, true)]
    [InlineData(0, false)]
    public void IsOutdated_Boundary(int ageDays, bool expected)
    {
        var releaseDate = new DateOnly(2026, 1, 1);
        var today = releaseDate.AddDays(ageDays);
        Assert.Equal(expected, YtDlpVersionParser.IsOutdated(releaseDate, today));
    }

    [Fact]
    public void IsOutdated_FutureReleaseDate_NegativeAge_False()
    {
        var releaseDate = new DateOnly(2026, 9, 26);
        var today = new DateOnly(2026, 1, 1);
        Assert.False(YtDlpVersionParser.IsOutdated(releaseDate, today));
    }

    [Fact]
    public void IsOutdated_YearBoundary_91Days_True()
    {
        var releaseDate = new DateOnly(2025, 12, 31);
        var today = new DateOnly(2026, 4, 1);
        Assert.True(YtDlpVersionParser.IsOutdated(releaseDate, today));
    }

    [Fact]
    public void TryParseOutdatedWarning_Matches()
    {
        var ok = YtDlpVersionParser.TryParseOutdatedWarning(
            "WARNING: Your yt-dlp version (2026.03.17) is older than 90 days!", out var version);
        Assert.True(ok);
        Assert.Equal("2026.03.17", version);
    }

    [Fact]
    public void TryParseOutdatedWarning_Nightly_Matches()
    {
        var ok = YtDlpVersionParser.TryParseOutdatedWarning(
            "WARNING: Your yt-dlp version (2026.08.19.232323) is older than 90 days!", out var version);
        Assert.True(ok);
        Assert.Equal("2026.08.19.232323", version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[download] 100%")]
    [InlineData("WARNING: [debugger] The JS runtime environment is broken.")]
    public void TryParseOutdatedWarning_OtherLines_False(string? line)
    {
        Assert.False(YtDlpVersionParser.TryParseOutdatedWarning(line, out _));
    }
}

using Xunit;
using YtDlpGui.Infrastructure;
using YtDlpGui.Models;

namespace YtDlpGui.Tests;

public class ProgressParserTests
{
    [Fact]
    public void TryParseProgress_RejectsNonProgressLine()
    {
        Assert.False(ProgressParser.TryParseProgress("[download] 12.3% of 45MiB", out _));
    }

    [Fact]
    public void TryParseProgress_ParsesAllFields()
    {
        // Real shape: percent_str has a leading space; bytes / speed / eta are integers/floats.
        var line = "PROGRESS: 12.3%|1234567|9876543|524288.0|45.2|3|10";
        var ok = ProgressParser.TryParseProgress(line, out var snap);
        Assert.True(ok);
        Assert.Equal(12.3, snap.Percent);
        Assert.Equal(1234567L, snap.DownloadedBytes);
        Assert.Equal(9876543L, snap.TotalBytes);
        Assert.Equal(524288.0, snap.SpeedBytesPerSec);
        Assert.Equal(45.2, snap.EtaSeconds);
        Assert.Equal(3, snap.FragmentIndex);
        Assert.Equal(10, snap.FragmentCount);
    }

    [Fact]
    public void TryParseProgress_NaFieldsBecomeNull()
    {
        var line = "PROGRESS: 12.3%|NA|NA|NA|NA|NA|NA";
        var ok = ProgressParser.TryParseProgress(line, out var snap);
        Assert.True(ok);
        Assert.Equal(12.3, snap.Percent);
        Assert.Null(snap.DownloadedBytes);
        Assert.Null(snap.TotalBytes);
        Assert.Null(snap.SpeedBytesPerSec);
        Assert.Null(snap.EtaSeconds);
        Assert.Null(snap.FragmentIndex);
        Assert.Null(snap.FragmentCount);
    }

    [Fact]
    public void TryParseProgress_PercentWithoutSign()
    {
        var line = "PROGRESS:50.0|0|0|0|0|0|0";
        var ok = ProgressParser.TryParseProgress(line, out var snap);
        Assert.True(ok);
        Assert.Equal(50.0, snap.Percent);
    }

    [Fact]
    public void TryParseProgress_TooFewFieldsRejected()
    {
        Assert.False(ProgressParser.TryParseProgress("PROGRESS:12%|1|2", out _));
    }

    [Theory]
    [InlineData("[download] just started", DownloadStatus.Downloading)]
    [InlineData("[Merger] merging output", DownloadStatus.Postprocessing)]
    [InlineData("[ffmpeg] running ffmpeg", DownloadStatus.Postprocessing)]
    [InlineData("[Metadata] writing metadata", DownloadStatus.Postprocessing)]
    [InlineData("[ThumbnailsConvertor] converting", DownloadStatus.Postprocessing)]
    [InlineData("[EmbedThumbnail] embedding", DownloadStatus.Postprocessing)]
    [InlineData("[ExtractAudio] extracting", DownloadStatus.Postprocessing)]
    [InlineData("[VideoConvertor] converting video", DownloadStatus.Postprocessing)]
    [InlineData("[FixupM3u8] fixing up", DownloadStatus.Postprocessing)]
    [InlineData("[info] resolving info", DownloadStatus.Resolving)]
    [InlineData("[youtube] xxx: extracting URL", DownloadStatus.Resolving)]
    [InlineData("[generic] foo: extracting", DownloadStatus.Resolving)]
    public void TryInferStatus_KnownTags(string line, DownloadStatus expected)
    {
        Assert.Equal(expected, ProgressParser.TryInferStatus(line));
    }

    [Fact]
    public void TryInferStatus_UnknownLine_ReturnsNull()
    {
        Assert.Null(ProgressParser.TryInferStatus("just a random line"));
    }

    [Fact]
    public void TryInferStatus_CaseSensitive()
    {
        // Stage tags are matched ordinally. Lowercase 'merger' must not match.
        Assert.Null(ProgressParser.TryInferStatus("[merger] something"));
    }
}

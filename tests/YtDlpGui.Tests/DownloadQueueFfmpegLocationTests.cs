using System.IO;
using Xunit;
using YtDlpGui.Models;
using YtDlpGui.Services;

namespace YtDlpGui.Tests;

/// <summary>
/// F2 production wiring: <see cref="DownloadQueue.ExecuteAsync"/> must resolve ffmpeg via
/// <see cref="IBinaryResolver.ResolveFfmpeg"/> and forward it into <c>ArgBuilder.Build</c> so
/// every real download gets <c>--ffmpeg-location</c>. ArgBuilderFfmpegLocationTests already
/// covers ArgBuilder itself in isolation; this covers the two lines in DownloadQueue that call it.
/// </summary>
public class DownloadQueueFfmpegLocationTests : IDisposable
{
    // A real file is needed so File.Exists(path) checks in DownloadQueue pass.
    private readonly string _fakeExePath = Path.GetTempFileName();
    private readonly string _outputFolder = Path.Combine(Path.GetTempPath(), "YtDlpGuiTests_" + Guid.NewGuid());

    public void Dispose()
    {
        try { File.Delete(_fakeExePath); } catch { /* best effort */ }
        try { Directory.Delete(_outputFolder, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ExecuteAsync_FfmpegResolved_PassesFfmpegLocationToArgv()
    {
        const string ffmpegPath = @"C:\tools\ffmpeg.exe";
        var runner = new CapturingRunner();
        using var queue = new DownloadQueue(new FakeResolver(_fakeExePath, ffmpegPath), runner);

        var finished = new TaskCompletionSource<bool>();
        queue.Finished += (_, _) => finished.TrySetResult(true);
        queue.Enqueue(NewItem());

        await finished.Task;

        Assert.NotNull(runner.LastArgs);
        var idx = runner.LastArgs!.ToList().IndexOf("--ffmpeg-location");
        Assert.True(idx >= 0, "expected --ffmpeg-location in argv");
        Assert.Equal(ffmpegPath, runner.LastArgs![idx + 1]);
    }

    [Fact]
    public async Task ExecuteAsync_FfmpegNotResolved_OmitsFlag()
    {
        var runner = new CapturingRunner();
        using var queue = new DownloadQueue(new FakeResolver(_fakeExePath, ffmpegPath: null), runner);

        var finished = new TaskCompletionSource<bool>();
        queue.Finished += (_, _) => finished.TrySetResult(true);
        queue.Enqueue(NewItem());

        await finished.Task;

        Assert.NotNull(runner.LastArgs);
        Assert.DoesNotContain("--ffmpeg-location", runner.LastArgs!);
    }

    private DownloadItem NewItem() => new()
    {
        Url = "https://example.com/v",
        // NfcNormalize is a GUI-side post-step unrelated to this test; turn it off so a
        // successful run doesn't also walk the (empty) output folder.
        Options = new DownloadOptions { OutputFolder = _outputFolder, NfcNormalize = false },
    };

    private sealed class FakeResolver : IBinaryResolver
    {
        private readonly string _ytDlpPath;
        private readonly string? _ffmpegPath;

        public FakeResolver(string ytDlpPath, string? ffmpegPath)
        {
            _ytDlpPath = ytDlpPath;
            _ffmpegPath = ffmpegPath;
        }

        public string? ResolveYtDlp() => _ytDlpPath;
        public string? ResolveFfmpeg() => _ffmpegPath;
    }

    private sealed class CapturingRunner : IYtDlpRunner
    {
        public IReadOnlyList<string>? LastArgs { get; private set; }

        public Task<YtDlpRunResult> RunAsync(
            string ytDlpPath,
            IReadOnlyList<string> args,
            string? workingDirectory,
            Action<string> onLine,
            CancellationToken ct)
        {
            LastArgs = args;
            return Task.FromResult(new YtDlpRunResult(0, false));
        }
    }
}

using System.IO;
using Xunit;
using YtDlpGui.Services;

namespace YtDlpGui.Tests;

/// <summary>
/// Exercises <see cref="YtDlpUpdater"/> against fake <see cref="IBinaryResolver"/> /
/// <see cref="IYtDlpRunner"/> implementations — never launches a real process.
/// </summary>
public class YtDlpUpdaterTests : IDisposable
{
    // A real file is needed so File.Exists(path) checks pass; content is irrelevant.
    private readonly string _fakeExePath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_fakeExePath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SelfUpdate_PassesDashUNoColor_NoWorkingDir()
    {
        var runner = new FakeRunner { Result = new YtDlpRunResult(0, false) };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.Equal(new[] { "-U", "--no-color" }, runner.LastArgs);
        Assert.Null(runner.LastWorkingDirectory);
    }

    [Fact]
    public async Task SelfUpdate_ExitZero_Success_MessageIsLastLine()
    {
        var runner = new FakeRunner
        {
            Lines = new[] { "Latest version: 2026.09.01, Current version: 2026.03.17", "Updated yt-dlp to 2026.09.01" },
            Result = new YtDlpRunResult(0, false),
        };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var r = await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.True(r.Success);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("Updated yt-dlp to 2026.09.01", r.Message);
    }

    [Fact]
    public async Task SelfUpdate_ErrorPrefix_Failure()
    {
        // Settings' existing rule: exit 0 + a literal "ERROR:" line still counts as failure.
        var runner = new FakeRunner
        {
            Lines = new[] { "ERROR: some failure" },
            Result = new YtDlpRunResult(0, true),
        };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var r = await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.False(r.Success);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task SelfUpdate_NonZeroExit_MessageIsLastErrorLine()
    {
        var runner = new FakeRunner
        {
            Lines = new[] { "some info", "ERROR: network unreachable" },
            Result = new YtDlpRunResult(1, false),
        };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var r = await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.False(r.Success);
        Assert.Equal(1, r.ExitCode);
        Assert.Equal("ERROR: network unreachable", r.Message);
    }

    [Fact]
    public async Task SelfUpdate_PipMessage_Failure()
    {
        var runner = new FakeRunner
        {
            Lines = new[] { "ERROR: You installed yt-dlp with pip or using the wheel from PyPi; Use that to update" },
            Result = new YtDlpRunResult(100, true),
        };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var r = await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.False(r.Success);
    }

    [Fact]
    public async Task SelfUpdate_PipMessage_NoErrorPrefixExitZero_StillFailure()
    {
        // Some package-manager installs print the same text without "ERROR:" and exit 0 —
        // the "Use that to update" guard must catch it regardless.
        var runner = new FakeRunner
        {
            Lines = new[] { "You installed yt-dlp with pip or using the wheel from PyPi; Use that to update" },
            Result = new YtDlpRunResult(0, false),
        };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var r = await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.False(r.Success);
    }

    [Fact]
    public async Task SelfUpdate_NotFound_NoProcess_ExitCodeNull()
    {
        var runner = new FakeRunner();
        var updater = new YtDlpUpdater(new FakeResolver(null), runner);

        var r = await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.False(r.Success);
        Assert.Null(r.ExitCode);
        Assert.False(runner.WasCalled);
        Assert.Equal("yt-dlp.exe not found.", r.Message);
    }

    [Fact]
    public async Task SelfUpdate_RunnerThrows_FailureWithMessage()
    {
        var runner = new FakeRunner { ThrowException = new InvalidOperationException("boom") };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var r = await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.False(r.Success);
        Assert.Null(r.ExitCode);
        Assert.Equal("boom", r.Message);
    }

    [Fact]
    public async Task SelfUpdate_Concurrent_SecondRejected_ThenResets()
    {
        var gate = new TaskCompletionSource<bool>();
        var runner = new FakeRunner { Gate = gate, Result = new YtDlpRunResult(0, false) };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        // Fires off but suspends inside the fake runner until we release the gate below.
        var first = updater.SelfUpdateAsync(CancellationToken.None);
        Assert.True(updater.IsUpdating);

        var second = await updater.SelfUpdateAsync(CancellationToken.None);
        Assert.False(second.Success);
        Assert.Null(second.ExitCode);
        Assert.Contains("already running", second.Message, StringComparison.OrdinalIgnoreCase);

        gate.SetResult(true);
        var firstResult = await first;

        Assert.True(firstResult.Success);
        Assert.False(updater.IsUpdating);
    }

    [Fact]
    public async Task SelfUpdate_RaisesIsUpdatingChanged_OnStartAndFinish()
    {
        var gate = new TaskCompletionSource<bool>();
        var runner = new FakeRunner { Gate = gate, Result = new YtDlpRunResult(0, false) };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var raisedCount = 0;
        var isUpdatingWhenFirstRaised = false;
        updater.IsUpdatingChanged += () =>
        {
            if (raisedCount == 0) isUpdatingWhenFirstRaised = updater.IsUpdating;
            raisedCount++;
        };

        var task = updater.SelfUpdateAsync(CancellationToken.None);

        Assert.Equal(1, raisedCount);
        Assert.True(isUpdatingWhenFirstRaised); // raised right after the flag flips to true, not before
        Assert.True(updater.IsUpdating);

        gate.SetResult(true);
        await task;

        Assert.Equal(2, raisedCount);
        Assert.False(updater.IsUpdating);
    }

    [Fact]
    public async Task SelfUpdate_RunnerThrows_RaisesIsUpdatingChanged_OnStartAndFinish_AndResets()
    {
        var gate = new TaskCompletionSource<bool>();
        var runner = new FakeRunner { Gate = gate, ThrowException = new InvalidOperationException("boom") };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var raisedCount = 0;
        var isUpdatingWhenFirstRaised = false;
        updater.IsUpdatingChanged += () =>
        {
            if (raisedCount == 0) isUpdatingWhenFirstRaised = updater.IsUpdating;
            raisedCount++;
        };

        var task = updater.SelfUpdateAsync(CancellationToken.None);

        Assert.Equal(1, raisedCount);
        Assert.True(isUpdatingWhenFirstRaised); // raised right after the flag flips to true, not before
        Assert.True(updater.IsUpdating);

        gate.SetResult(true);
        var r = await task;

        Assert.False(r.Success);
        Assert.Null(r.ExitCode);
        Assert.Equal("boom", r.Message);
        Assert.Equal(2, raisedCount); // start + finally, even though RunAsync threw
        Assert.False(updater.IsUpdating);
    }

    [Fact]
    public async Task SelfUpdate_ThrowingSubscriber_DoesNotThrow_AndIsUpdatingResetsToFalse()
    {
        var runner = new FakeRunner { Result = new YtDlpRunResult(0, false) };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        updater.IsUpdatingChanged += () => throw new InvalidOperationException("subscriber boom");

        var r = await updater.SelfUpdateAsync(CancellationToken.None);

        Assert.True(r.Success);
        Assert.False(updater.IsUpdating);
    }

    [Fact]
    public async Task SelfUpdate_RejectedConcurrentCall_DoesNotRaiseIsUpdatingChanged()
    {
        var gate = new TaskCompletionSource<bool>();
        var runner = new FakeRunner { Gate = gate, Result = new YtDlpRunResult(0, false) };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var raisedCount = 0;
        var first = updater.SelfUpdateAsync(CancellationToken.None);
        updater.IsUpdatingChanged += () => raisedCount++;

        var second = await updater.SelfUpdateAsync(CancellationToken.None);
        Assert.False(second.Success);
        Assert.Equal(0, raisedCount); // the rejected call never touched the flag

        gate.SetResult(true);
        await first;
        Assert.Equal(1, raisedCount); // only the finally of the call that actually ran
    }

    [Fact]
    public async Task CheckVersion_ParsesVersion_ArgsAreDashDashVersion()
    {
        var runner = new FakeRunner { Lines = new[] { "2026.03.17" }, Result = new YtDlpRunResult(0, false) };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var check = await updater.CheckVersionAsync(CancellationToken.None);

        Assert.Equal(_fakeExePath, check.YtDlpPath);
        Assert.Equal("2026.03.17", check.Version);
        Assert.Equal(new DateOnly(2026, 3, 17), check.ReleaseDate);
        Assert.Equal(new[] { "--version" }, runner.LastArgs);
    }

    [Fact]
    public async Task CheckVersion_Unparseable_VersionNull()
    {
        var runner = new FakeRunner { Lines = new[] { "not a version" }, Result = new YtDlpRunResult(0, false) };
        var updater = new YtDlpUpdater(new FakeResolver(_fakeExePath), runner);

        var check = await updater.CheckVersionAsync(CancellationToken.None);

        Assert.Equal(_fakeExePath, check.YtDlpPath);
        Assert.Null(check.Version);
        Assert.Null(check.ReleaseDate);
    }

    [Fact]
    public async Task CheckVersion_NotFound_PathNull()
    {
        var runner = new FakeRunner();
        var updater = new YtDlpUpdater(new FakeResolver(null), runner);

        var check = await updater.CheckVersionAsync(CancellationToken.None);

        Assert.Null(check.YtDlpPath);
        Assert.Null(check.Version);
        Assert.False(runner.WasCalled);
    }

    private sealed class FakeResolver : IBinaryResolver
    {
        private readonly string? _ytDlpPath;

        public FakeResolver(string? ytDlpPath) => _ytDlpPath = ytDlpPath;

        public string? ResolveYtDlp() => _ytDlpPath;
        public string? ResolveFfmpeg() => null;
    }

    private sealed class FakeRunner : IYtDlpRunner
    {
        public string[] Lines { get; set; } = Array.Empty<string>();
        public YtDlpRunResult Result { get; set; } = new(0, false);
        public Exception? ThrowException { get; set; }

        /// <summary>When set, RunAsync suspends here until the test completes it — used to
        /// simulate an in-flight update for the concurrency test.</summary>
        public TaskCompletionSource<bool>? Gate { get; set; }

        public bool WasCalled { get; private set; }
        public IReadOnlyList<string>? LastArgs { get; private set; }
        public string? LastWorkingDirectory { get; private set; }

        public async Task<YtDlpRunResult> RunAsync(
            string ytDlpPath,
            IReadOnlyList<string> args,
            string? workingDirectory,
            Action<string> onLine,
            CancellationToken ct)
        {
            WasCalled = true;
            LastArgs = args;
            LastWorkingDirectory = workingDirectory;

            if (Gate is not null) await Gate.Task.ConfigureAwait(false);
            if (ThrowException is not null) throw ThrowException;

            foreach (var line in Lines) onLine(line);
            return Result;
        }
    }
}

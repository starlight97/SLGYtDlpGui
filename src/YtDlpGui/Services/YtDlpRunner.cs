using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace YtDlpGui.Services;

public sealed class YtDlpRunner : IYtDlpRunner
{
    public async Task<YtDlpRunResult> RunAsync(
        string ytDlpPath,
        IReadOnlyList<string> args,
        string? workingDirectory,
        Action<string> onLine,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // yt-dlp emits UTF-8; force the streams off cp949 (Windows-Korean default).
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // Channel serializes interleaved stdout/stderr lines so the consumer sees them in order.
        var ch = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var sawErrorPrefix = false;

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            ch.Writer.TryWrite(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            // Per SPEC §13: stderr is normal info; only the literal "ERROR:" prefix counts as failure.
            if (e.Data.StartsWith("ERROR:", StringComparison.Ordinal)) sawErrorPrefix = true;
            ch.Writer.TryWrite(e.Data);
        };

        if (!proc.Start())
            throw new InvalidOperationException($"Failed to start process: {ytDlpPath}");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Drain channel on a background task so the caller can await it together with process exit.
        var drainTask = Task.Run(async () =>
        {
            await foreach (var line in ch.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
                onLine(line);
        }, CancellationToken.None);

        try
        {
            using var reg = ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
                catch { /* already gone */ }
            });

            await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            // Flush remaining buffered async reads.
            try { proc.WaitForExit(); } catch { }
            ch.Writer.TryComplete();
            try { await drainTask.ConfigureAwait(false); } catch { }
        }

        return new YtDlpRunResult(proc.ExitCode, sawErrorPrefix);
    }
}

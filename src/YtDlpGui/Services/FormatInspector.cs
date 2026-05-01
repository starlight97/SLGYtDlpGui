using System.IO;
using System.Text;
using System.Text.Json;
using YtDlpGui.Models;

namespace YtDlpGui.Services;

public sealed class FormatInspector : IFormatInspector
{
    private readonly IBinaryResolver _binaries;
    private readonly IYtDlpRunner _runner;

    public FormatInspector(IBinaryResolver binaries, IYtDlpRunner runner)
    {
        _binaries = binaries;
        _runner = runner;
    }

    public async Task<FormatInspectionResult> InspectAsync(string url, CancellationToken ct)
    {
        var ytDlpPath = _binaries.ResolveYtDlp();
        if (ytDlpPath is null || !File.Exists(ytDlpPath))
            throw new FileNotFoundException("yt-dlp.exe not found. Set the path in Settings.");

        // Mirror the always-on prefix from SPEC §4 — minus the progress template
        // (no download here, no progress lines to parse).
        var args = new[]
        {
            "--ignore-config",
            "--no-color",
            "--no-warnings",
            "--no-playlist",
            "--skip-download",
            "--dump-single-json",
            url,
        };

        // yt-dlp emits JSON on stdout. Status / warnings can land on stderr; we
        // collect everything but only treat the contents from the first '{' as JSON.
        var sb = new StringBuilder();
        var result = await _runner.RunAsync(
            ytDlpPath,
            args,
            workingDirectory: null,
            onLine: line => sb.AppendLine(line),
            ct: ct).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.SawErrorPrefix)
        {
            // Surface the last few lines so the dialog can show why.
            var tail = TailLines(sb.ToString(), 6);
            throw new InvalidOperationException(
                $"yt-dlp exited with code {result.ExitCode}.{(tail.Length > 0 ? "\n" + tail : "")}");
        }

        var raw = sb.ToString();
        var firstBrace = raw.IndexOf('{');
        if (firstBrace < 0) throw new InvalidOperationException("yt-dlp produced no JSON output.");
        // Trim everything before the first '{' (e.g. "[generic] ...").
        var json = raw.Substring(firstBrace);

        return Parse(json);
    }

    public static FormatInspectionResult Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var title = GetString(root, "title");
        long? duration = TryGetDouble(root, "duration") is double d ? (long?)d : null;

        var list = new List<FormatInfo>();
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in formats.EnumerateArray())
            {
                var resolution = GetString(f, "resolution");
                if (resolution.Length == 0) resolution = ComposeResolution(f);

                list.Add(new FormatInfo
                {
                    Id = GetString(f, "format_id"),
                    Ext = GetString(f, "ext"),
                    Resolution = resolution,
                    Fps = TryGetDouble(f, "fps"),
                    Vcodec = GetString(f, "vcodec"),
                    Acodec = GetString(f, "acodec"),
                    Filesize = TryGetLong(f, "filesize") ?? TryGetLong(f, "filesize_approx"),
                    Tbr = TryGetDouble(f, "tbr"),
                    Note = GetString(f, "format_note"),
                    Protocol = GetString(f, "protocol"),
                });
            }
        }
        return new FormatInspectionResult(
            string.IsNullOrEmpty(title) ? null : title,
            duration,
            list);
    }

    private static string GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static double? TryGetDouble(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
    }

    private static long? TryGetLong(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;
    }

    private static string ComposeResolution(JsonElement f)
    {
        var w = TryGetLong(f, "width");
        var h = TryGetLong(f, "height");
        return (w, h) switch
        {
            ({ } ww, { } hh) => $"{ww}x{hh}",
            (_, { } hh) => $"{hh}p",
            _ => string.Empty,
        };
    }

    private static string TailLines(string text, int n)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= n) return string.Join('\n', lines).Trim();
        return string.Join('\n', lines[^n..]).Trim();
    }
}

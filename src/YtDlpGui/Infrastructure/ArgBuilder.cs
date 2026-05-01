using System.Globalization;
using YtDlpGui.Models;

namespace YtDlpGui.Infrastructure;

/// <summary>
/// Maps <see cref="DownloadOptions"/> + URL onto a yt-dlp argv (string[]).
/// Per SPEC §7: empty/default values aren't emitted; output is consumed via
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> so the caller
/// never has to reason about Windows quoting.
/// </summary>
public static class ArgBuilder
{
    /// <summary>Mandatory always-on prefix per SPEC §4 / §7.</summary>
    public const string ProgressTemplate =
        "PROGRESS:%(progress._percent_str)s|%(progress._downloaded_bytes)s|%(progress._total_bytes)s|%(progress._speed)s|%(progress._eta_seconds)s|%(progress.fragment_index)s|%(progress.fragment_count)s";

    public static string[] Build(DownloadOptions o, string url)
    {
        var args = new List<string>(32);

        // §4 mandatory prefix.
        args.Add("--ignore-config");
        args.Add("--no-color");
        args.Add("--newline");
        args.Add("--no-progress");
        args.Add("--progress-template");
        args.Add(ProgressTemplate);

        AppendOutput(args, o);
        AppendFormat(args, o);
        AppendContainer(args, o);
        AppendSubtitles(args, o);
        AppendAudio(args, o);
        AppendNetwork(args, o);
        AppendPlaylist(args, o);
        AppendMisc(args, o);

        // URL last so any preceding flag is unambiguous.
        args.Add(url);
        return args.ToArray();
    }

    private static void AppendOutput(List<string> args, DownloadOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.OutputFolder))
        {
            args.Add("-P");
            args.Add(o.OutputFolder);
        }
        if (!string.IsNullOrWhiteSpace(o.OutputTemplate))
        {
            args.Add("-o");
            args.Add(o.OutputTemplate);
        }
        if (o.RestrictFilenames) args.Add("--restrict-filenames");
        if (o.WindowsFilenames) args.Add("--windows-filenames");
        // NfcNormalize is a GUI-side post-step, not a yt-dlp flag — handled by NfcNormalizer.
    }

    private static void AppendFormat(List<string> args, DownloadOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.FormatSelector))
        {
            args.Add("-f");
            args.Add(o.FormatSelector);
        }
    }

    private static void AppendContainer(List<string> args, DownloadOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.MergeOutputFormat))
        {
            args.Add("--merge-output-format");
            args.Add(o.MergeOutputFormat);
        }
        if (o.EmbedMetadata) args.Add("--embed-metadata");
        if (o.EmbedThumbnail) args.Add("--embed-thumbnail");
        if (o.EmbedChapters) args.Add("--embed-chapters");
        if (o.WriteDescription) args.Add("--write-description");
        if (o.WriteInfoJson) args.Add("--write-info-json");
    }

    private static void AppendSubtitles(List<string> args, DownloadOptions o)
    {
        if (o.WriteSubs) args.Add("--write-subs");
        if (o.WriteAutoSubs) args.Add("--write-auto-subs");
        if (o.EmbedSubs) args.Add("--embed-subs");
        if (!string.IsNullOrWhiteSpace(o.SubLangs))
        {
            args.Add("--sub-langs");
            args.Add(o.SubLangs);
        }
        if (!string.IsNullOrWhiteSpace(o.ConvertSubsTo))
        {
            args.Add("--convert-subs");
            args.Add(o.ConvertSubsTo);
        }
    }

    private static void AppendAudio(List<string> args, DownloadOptions o)
    {
        if (!o.ExtractAudio) return;
        args.Add("-x");
        if (!string.IsNullOrWhiteSpace(o.AudioCodec))
        {
            args.Add("--audio-format");
            args.Add(o.AudioCodec);
        }
        if (!string.IsNullOrWhiteSpace(o.AudioQuality))
        {
            args.Add("--audio-quality");
            args.Add(o.AudioQuality);
        }
        if (o.KeepVideo) args.Add("--keep-video");
    }

    private static void AppendNetwork(List<string> args, DownloadOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.CookiesFromBrowser))
        {
            args.Add("--cookies-from-browser");
            args.Add(string.IsNullOrWhiteSpace(o.CookiesBrowserProfile)
                ? o.CookiesFromBrowser
                : $"{o.CookiesFromBrowser}:{o.CookiesBrowserProfile}");
        }
        if (!string.IsNullOrWhiteSpace(o.CookiesFile))
        {
            args.Add("--cookies");
            args.Add(o.CookiesFile);
        }
        if (!string.IsNullOrWhiteSpace(o.Proxy))
        {
            args.Add("--proxy");
            args.Add(o.Proxy);
        }
        if (!string.IsNullOrWhiteSpace(o.RateLimit))
        {
            args.Add("--limit-rate");
            args.Add(o.RateLimit);
        }
        if (o.ConcurrentFragments is int cf and > 0)
        {
            args.Add("--concurrent-fragments");
            args.Add(cf.ToString(CultureInfo.InvariantCulture));
        }
        if (o.Retries is int r and >= 0)
        {
            args.Add("--retries");
            args.Add(r.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendPlaylist(List<string> args, DownloadOptions o)
    {
        switch (o.DownloadPlaylist)
        {
            case true: args.Add("--yes-playlist"); break;
            case false: args.Add("--no-playlist"); break;
            // null: don't pass anything; let yt-dlp's default decide.
        }
        if (!string.IsNullOrWhiteSpace(o.PlaylistItems))
        {
            args.Add("--playlist-items");
            args.Add(o.PlaylistItems);
        }
        if (o.PlaylistReverse) args.Add("--playlist-reverse");
        if (o.PlaylistRandom) args.Add("--playlist-random");
    }

    private static void AppendMisc(List<string> args, DownloadOptions o)
    {
        if (o.ForceOverwrites) args.Add("--force-overwrites");
        if (o.ContinuePartial) args.Add("--continue");
        if (o.Verbose) args.Add("-v");

        if (!string.IsNullOrWhiteSpace(o.ExtraArgs))
        {
            // Free-form: split on whitespace. The user is responsible for not using
            // spaces inside an arg here — that's what the bound options are for.
            foreach (var tok in o.ExtraArgs.Split(' ', '\t', '\r', '\n'))
            {
                if (!string.IsNullOrWhiteSpace(tok)) args.Add(tok);
            }
        }
    }

    /// <summary>Reproducible single-line representation of an argv. For per-item logs.</summary>
    public static string FormatForLog(string exePath, IReadOnlyList<string> args)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Quote(exePath));
        foreach (var a in args)
        {
            sb.Append(' ').Append(Quote(a));
        }
        return sb.ToString();

        static string Quote(string s)
        {
            if (s.Length == 0) return "\"\"";
            return (s.Contains(' ') || s.Contains('"'))
                ? "\"" + s.Replace("\"", "\\\"") + "\""
                : s;
        }
    }
}

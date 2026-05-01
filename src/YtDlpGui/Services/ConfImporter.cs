using System.Globalization;
using System.IO;
using System.Text;
using YtDlpGui.Models;

namespace YtDlpGui.Services;

/// <summary>
/// SPEC §4.1: one-way <c>yt-dlp.conf</c> importer. Mirrors yt-dlp's
/// <c>_read_user_conf</c> in spirit — strip comments, shlex-split, walk argv.
/// Only flags the GUI has UI for are applied; everything else is reported back
/// for the user to review.
/// </summary>
public sealed class ConfImporter : IConfImporter
{
    public ConfImportResult Import(string filePath)
    {
        var text = File.ReadAllText(filePath);
        return ImportText(text);
    }

    public ConfImportResult ImportText(string text)
    {
        var tokens = Tokenize(text);
        var opts = new DownloadOptions();
        var applied = new List<string>();
        var unrecognized = new List<string>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var raw = tokens[i];
            if (raw.Length == 0 || !raw.StartsWith('-'))
                continue; // positional (URL etc.) — yt-dlp ignores those in conf, so do we.

            string flag = raw;
            string? equalValue = null;
            var eq = raw.IndexOf('=');
            if (eq > 1)
            {
                flag = raw[..eq];
                equalValue = raw[(eq + 1)..];
            }

            if (SilentlyIgnored.Contains(flag))
                continue;

            if (BooleanMap.TryGetValue(flag, out var boolApply))
            {
                boolApply(opts);
                applied.Add(flag);
                continue;
            }

            if (ValueMap.TryGetValue(flag, out var valueApply))
            {
                var value = equalValue ?? PeekValue(tokens, ref i);
                if (value is null)
                {
                    unrecognized.Add($"{flag} (missing value)");
                    continue;
                }
                valueApply(opts, value);
                applied.Add($"{flag} {QuoteIfNeeded(value)}");
                continue;
            }

            // Unknown. Heuristic: if the next token isn't a flag, it's probably this one's value.
            var unknownValue = equalValue;
            if (unknownValue is null && i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
                unknownValue = tokens[++i];

            unrecognized.Add(unknownValue is null ? flag : $"{flag} {QuoteIfNeeded(unknownValue)}");
        }

        return new ConfImportResult(opts, applied, unrecognized);
    }

    private static string? PeekValue(IReadOnlyList<string> tokens, ref int i)
    {
        if (i + 1 >= tokens.Count) return null;
        var next = tokens[i + 1];
        // If the next token already looks like a flag, treat as missing value.
        // (--foo --bar means --foo took no value.)
        if (next.StartsWith('-') && next.Length > 1 && !char.IsDigit(next[1]) && next[1] != '.')
            return null;
        i++;
        return next;
    }

    /// <summary>
    /// Strip whole-line and trailing-<c>#</c> comments, then shlex-split each
    /// non-empty line. Outer list is flat — yt-dlp doesn't care about line
    /// boundaries inside a conf file.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string text)
    {
        var result = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            // Strip trailing comment. Match yt-dlp's behavior: split on the
            // first '#' regardless of quoting (which is what _read_user_conf does).
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash].TrimEnd();
            if (line.Length == 0) continue;

            ShlexSplit(line, result);
        }
        return result;
    }

    private static void ShlexSplit(string line, List<string> into)
    {
        var sb = new StringBuilder();
        bool inSingle = false, inDouble = false;
        bool tokenStarted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inSingle)
            {
                if (c == '\'') inSingle = false;
                else { sb.Append(c); }
            }
            else if (inDouble)
            {
                if (c == '"') inDouble = false;
                else if (c == '\\' && i + 1 < line.Length)
                {
                    var n = line[i + 1];
                    if (n == '"' || n == '\\' || n == '$' || n == '`')
                    {
                        sb.Append(n);
                        i++;
                    }
                    else sb.Append(c);
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '\'') { inSingle = true; tokenStarted = true; }
                else if (c == '"') { inDouble = true; tokenStarted = true; }
                else if (c == '\\' && i + 1 < line.Length)
                {
                    sb.Append(line[++i]);
                    tokenStarted = true;
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (tokenStarted)
                    {
                        into.Add(sb.ToString());
                        sb.Clear();
                        tokenStarted = false;
                    }
                }
                else
                {
                    sb.Append(c);
                    tokenStarted = true;
                }
            }
        }
        if (tokenStarted) into.Add(sb.ToString());
    }

    private static string QuoteIfNeeded(string s)
    {
        if (s.Length == 0) return "\"\"";
        return s.Any(c => char.IsWhiteSpace(c) || c == '"') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;
    }

    // --- Mappings ---

    /// <summary>Always-on prefix flags from §4 — would conflict with the GUI's contract, drop silently.</summary>
    private static readonly HashSet<string> SilentlyIgnored = new(StringComparer.Ordinal)
    {
        "--ignore-config", "--no-color", "--newline", "--no-progress",
        "--progress-template",
        "--color",                       // we force --no-color
        "--no-newline",                  // we force --newline
        "--config-location",             // we deliberately ignore conf-of-conf chains
    };

    /// <summary>Boolean flags (no value).</summary>
    private static readonly Dictionary<string, Action<DownloadOptions>> BooleanMap = new(StringComparer.Ordinal)
    {
        ["--restrict-filenames"]    = o => o.RestrictFilenames = true,
        ["--no-restrict-filenames"] = o => o.RestrictFilenames = false,
        ["--windows-filenames"]     = o => o.WindowsFilenames = true,
        ["--no-windows-filenames"]  = o => o.WindowsFilenames = false,

        ["--embed-metadata"]        = o => o.EmbedMetadata = true,
        ["--add-metadata"]          = o => o.EmbedMetadata = true,
        ["--no-embed-metadata"]     = o => o.EmbedMetadata = false,
        ["--no-add-metadata"]       = o => o.EmbedMetadata = false,
        ["--embed-thumbnail"]       = o => o.EmbedThumbnail = true,
        ["--no-embed-thumbnail"]    = o => o.EmbedThumbnail = false,
        ["--embed-chapters"]        = o => o.EmbedChapters = true,
        ["--no-embed-chapters"]     = o => o.EmbedChapters = false,
        ["--write-description"]     = o => o.WriteDescription = true,
        ["--no-write-description"]  = o => o.WriteDescription = false,
        ["--write-info-json"]       = o => o.WriteInfoJson = true,
        ["--no-write-info-json"]    = o => o.WriteInfoJson = false,

        ["--write-subs"]            = o => o.WriteSubs = true,
        ["--write-subtitles"]       = o => o.WriteSubs = true,
        ["--no-write-subs"]         = o => o.WriteSubs = false,
        ["--no-write-subtitles"]    = o => o.WriteSubs = false,
        ["--write-auto-subs"]       = o => o.WriteAutoSubs = true,
        ["--write-auto-subtitles"]  = o => o.WriteAutoSubs = true,
        ["--no-write-auto-subs"]    = o => o.WriteAutoSubs = false,
        ["--no-write-auto-subtitles"]= o => o.WriteAutoSubs = false,
        ["--embed-subs"]            = o => o.EmbedSubs = true,
        ["--no-embed-subs"]         = o => o.EmbedSubs = false,

        ["-x"]                      = o => o.ExtractAudio = true,
        ["--extract-audio"]         = o => o.ExtractAudio = true,
        ["-k"]                      = o => o.KeepVideo = true,
        ["--keep-video"]            = o => o.KeepVideo = true,
        ["--no-keep-video"]         = o => o.KeepVideo = false,

        ["--yes-playlist"]          = o => o.DownloadPlaylist = true,
        ["--no-playlist"]           = o => o.DownloadPlaylist = false,
        ["--playlist-reverse"]      = o => o.PlaylistReverse = true,
        ["--no-playlist-reverse"]   = o => o.PlaylistReverse = false,
        ["--playlist-random"]       = o => o.PlaylistRandom = true,
        ["--no-playlist-random"]    = o => o.PlaylistRandom = false,

        ["--force-overwrites"]      = o => o.ForceOverwrites = true,
        ["--no-force-overwrites"]   = o => o.ForceOverwrites = false,
        ["-c"]                      = o => o.ContinuePartial = true,
        ["--continue"]              = o => o.ContinuePartial = true,
        ["--no-continue"]           = o => o.ContinuePartial = false,
        ["-v"]                      = o => o.Verbose = true,
        ["--verbose"]               = o => o.Verbose = true,
    };

    /// <summary>Flags that consume the next token (or use --flag=value form).</summary>
    private static readonly Dictionary<string, Action<DownloadOptions, string>> ValueMap = new(StringComparer.Ordinal)
    {
        ["-P"]                      = (o, v) => o.OutputFolder = StripPathPrefix(v),
        ["--paths"]                 = (o, v) => o.OutputFolder = StripPathPrefix(v),
        ["-o"]                      = (o, v) => o.OutputTemplate = v,
        ["--output"]                = (o, v) => o.OutputTemplate = v,
        ["-f"]                      = (o, v) => o.FormatSelector = v,
        ["--format"]                = (o, v) => o.FormatSelector = v,
        ["--merge-output-format"]   = (o, v) => o.MergeOutputFormat = v,

        ["--sub-langs"]             = (o, v) => o.SubLangs = v,
        ["--sub-lang"]              = (o, v) => o.SubLangs = v,
        ["--convert-subs"]          = (o, v) => o.ConvertSubsTo = v,
        ["--convert-subtitles"]     = (o, v) => o.ConvertSubsTo = v,

        ["--audio-format"]          = (o, v) => o.AudioCodec = v,
        ["--audio-quality"]         = (o, v) => o.AudioQuality = v,

        ["--cookies"]               = (o, v) => o.CookiesFile = v,
        ["--cookies-from-browser"]  = (o, v) => SetBrowserCookies(o, v),
        ["--proxy"]                 = (o, v) => o.Proxy = v,
        ["-r"]                      = (o, v) => o.RateLimit = v,
        ["--limit-rate"]            = (o, v) => o.RateLimit = v,
        ["-N"]                      = (o, v) => o.ConcurrentFragments = ParseInt(v),
        ["--concurrent-fragments"]  = (o, v) => o.ConcurrentFragments = ParseInt(v),
        ["-R"]                      = (o, v) => o.Retries = ParseInt(v),
        ["--retries"]               = (o, v) => o.Retries = ParseInt(v),
        ["-I"]                      = (o, v) => o.PlaylistItems = v,
        ["--playlist-items"]        = (o, v) => o.PlaylistItems = v,
    };

    private static string StripPathPrefix(string v)
    {
        // yt-dlp's --paths supports "TYPE:PATH" form (e.g. "home:D:\dl"). We
        // care about the "home" / generic path; strip a single leading "TYPE:"
        // when present.
        var idx = v.IndexOf(':');
        if (idx <= 0 || idx > 12) return v; // arbitrary cap to avoid eating "C:\" drive prefixes
        // If the part before ':' looks like a Windows drive letter (single char), keep the whole string.
        var prefix = v[..idx];
        if (prefix.Length == 1) return v;
        return v[(idx + 1)..];
    }

    private static void SetBrowserCookies(DownloadOptions o, string v)
    {
        // Format: BROWSER[+KEYRING][:PROFILE][::CONTAINER]
        // We capture just BROWSER and PROFILE; the rest goes back into Browser if we don't recognize it.
        var idx = v.IndexOf(':');
        if (idx < 0)
        {
            o.CookiesFromBrowser = v;
            o.CookiesBrowserProfile = string.Empty;
        }
        else
        {
            o.CookiesFromBrowser = v[..idx];
            o.CookiesBrowserProfile = v[(idx + 1)..];
        }
    }

    private static int? ParseInt(string v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

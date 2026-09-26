# YtDlpGui.Tests

> 한국어: [README.ko.md](README.ko.md)

Unit tests for the pure-logic components of YtDlpGui — the parts that have no
UI dependency and no live yt-dlp invocation, so they can be exercised in
isolation in milliseconds.

## Why this exists

The main project is a WPF app, but a non-trivial chunk of its behavior lives
in classes that are pure functions in everything but name: input goes in,
output comes out, no UI thread, no network. Four of those classes happen to
also be the *contract* between the GUI and the `yt-dlp` CLI:

- Get them wrong and downloads break in subtle ways.
- They have edge cases — `NA` values, quoting, empty fields, tri-state booleans,
  Unicode normalization — that are easy to regress when you touch nearby code.
- They are orders of magnitude faster to iterate on via `dotnet test` than
  by clicking through the UI.

So we pin them down with tests. Everything else (XAML bindings, dialog flow,
the actual yt-dlp subprocess) stays out of scope here on purpose.

## What's covered

| Target | File | Tests | Verifies |
|---|---|---|---|
| `Infrastructure.ArgBuilder` | [ArgBuilderTests.cs](ArgBuilderTests.cs) | 19 | `DownloadOptions` → yt-dlp argv. SPEC §4 always-on prefix, empty-field omission, `--cookies-from-browser browser:profile` composition, tri-state playlist, `ExtraArgs` whitespace split, subtitle full-suite, `FormatForLog` quoting. |
| `Infrastructure.ProgressParser` | [ProgressParserTests.cs](ProgressParserTests.cs) | 19 | `PROGRESS:` line parsing, `NA` → `null` field handling, malformed/short lines rejected, status inference from 12 `[stage]` tags (`[download]`, `[Merger]`, `[ffmpeg]`, etc.), case-sensitive matching. |
| `Services.ConfImporter` | [ConfImporterTests.cs](ConfImporterTests.cs) | 23 | yt-dlp.conf parsing: `--flag=value` syntax, comments, single/double quotes, `--no-` negation, aliases (`--add-metadata` → embed-metadata), tri-state playlist, `--cookies-from-browser browser:profile`, always-on flags silently dropped. Also documents the known shlex backslash limitation. |
| `Infrastructure.Hangul.NfcNormalizer` | [NfcNormalizerTests.cs](NfcNormalizerTests.cs) | 5 | NFD → NFC filename rename, no-op when already NFC, missing-file no-op, folder sweep with `modifiedSince` filter. |

**Total: 66 tests**, all passing.

## What's NOT covered

- **WPF UI** — view models, bindings, dialogs. UI automation needs a separate
  harness (FlaUI, Appium); that's a v1.0+ concern.
- **`YtDlpRunner` subprocess invocation** — calls a real `yt-dlp.exe`. Real
  integration tests would need a sample video and network access, so they're
  deferred.
- **Settings / profile store file I/O** — exercised on every app launch
  anyway; explicit tests would be low value vs. effort.

## Running

```bash
# All tests
dotnet test YtDlpGui.sln

# One class
dotnet test YtDlpGui.sln --filter "FullyQualifiedName~ArgBuilderTests"

# One method
dotnet test YtDlpGui.sln --filter "Name=Build_DownloadPlaylistTrue_EmitsYes"

# Coverage (needs coverlet — optional)
dotnet test YtDlpGui.sln --collect:"XPlat Code Coverage"
```

In Visual Studio or Rider the Test Explorer auto-discovers them; the play
icon next to each method runs that one test.

## Adding a new test

1. The target must be a non-UI class. If it touches `System.Windows.*` or
   needs a `Dispatcher`, it doesn't belong here.
2. Find the matching `*Tests.cs` for that class, or create a new file
   alongside the existing four if you're covering a new pure class.
3. Use `[Fact]` for one-off cases, `[Theory] + [InlineData(...)]` for
   parameterized variants (see `ProgressParserTests.TryInferStatus_KnownTags`
   for an example).
4. Name pattern: `Method_Scenario_ExpectedOutcome` —
   e.g. `Build_CookiesFromBrowserWithProfile_EmitsBrowserColonProfile`.

Skeleton:

```csharp
[Fact]
public void Build_NewFlag_EmittedWhenSet()
{
    var opts = new DownloadOptions { /* ... */ };
    var args = ArgBuilder.Build(opts, "https://x");
    Assert.Contains("--new-flag", args);
}
```

## Project config notes

- `TargetFramework=net10.0-windows` and `UseWPF=true` to match the main
  project's TFM — keeps `ProjectReference` resolution clean even though
  no test actually pulls a WPF type.
- `IsPackable=false` so this project never ends up in a NuGet output.
- `NfcNormalizerTests` writes to a per-test-run directory under
  `%TEMP%\YtDlpGuiTests-<guid>` and cleans up on `Dispose`. If a run is
  killed mid-test, leftover folders are safe to delete by hand.
- `ProgressParserTests` Theory covers every stage tag currently recognized
  by `ProgressParser.TryInferStatus`. When you add a new tag mapping, add
  the matching `[InlineData]` row in the same commit.

## Relationship to the SPEC

The tests intentionally encode a few SPEC-level invariants so a regression
flips a test red rather than waiting until someone notices a broken
download:

- SPEC §4 / §7: the always-on prefix (`--ignore-config`, `--no-color`,
  `--newline`, `--no-progress`, `--progress-template`) is asserted on every
  `ArgBuilder.Build` call.
- SPEC §6: `PROGRESS:` template parsing — `NA` values become null, not
  zero, not exception.
- SPEC §7: empty / default options aren't emitted (keeps the command line
  short and inspectable).
- SPEC §13: stderr `ERROR:` semantics are out of scope here (lives in
  `YtDlpRunner`), but stage-tag → status inference is asserted.
- SPEC §4.1: conf import — known flags map onto options, unknown go to a
  separate `UnrecognizedFlags` list, always-on flags drop silently.
- SPEC §8: NFC filename normalization round-trip.

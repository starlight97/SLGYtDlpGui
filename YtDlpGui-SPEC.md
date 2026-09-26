# YtDlpGui — Specification

**Owner:** Jeong
**Target platform:** Windows 10/11 x64
**Last updated:** 2026-05-02

---

## 1. Goal

A native Windows GUI front-end for `yt-dlp` aimed at an **expert user** who knows
yt-dlp's CLI and wants:

- Faster batch / queue workflows than typing commands
- Visibility into multiple concurrent downloads (progress, speed, ETA, log)
- Full control — every meaningful yt-dlp option exposed in the UI, no hand-holding
- A clean, deterministic invocation contract (no hidden config interactions)

This is a personal-use tool first. Distribution is a non-goal for v1.

---

## 2. Tech stack

| Layer | Choice | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | Modern, supported, single-file publish |
| UI | **WPF + XAML** | Native Windows, leverages C# experience |
| MVVM | **CommunityToolkit.Mvvm** | `[ObservableProperty]`, `[RelayCommand]` source-gen — very low ceremony |
| Theming | **WPF-UI (lepoco/wpfui)** | Fluent / WinUI3-style controls, dark mode out of the box |
| Logging | **Serilog** (file + in-memory sink) | App-side logs separate from yt-dlp output |
| Settings | **System.Text.Json** → `%LOCALAPPDATA%\YtDlpGui\settings.json` | No registry, easy to inspect |
| Process mgmt | `System.Diagnostics.Process` with async stdout/stderr | Standard, no third-party needed |

External binaries (not bundled, resolved by path or lookup):
- `yt-dlp.exe`
- `ffmpeg.exe`

---

## 3. Architecture

MVVM. Strict separation of view/VM/services. No code-behind beyond `InitializeComponent` and tiny visual glue.

```
YtDlpGui/
├─ App.xaml(.cs)                   // DI container bootstrap
├─ Views/
│   ├─ MainWindow.xaml             // shell, queue, log, command bar
│   ├─ Controls/
│   │   ├─ DownloadItemView.xaml   // one row in the queue
│   │   ├─ FormatPickerView.xaml   // -F inspector + preset selector
│   │   └─ OptionsPanel.xaml       // all yt-dlp options exposed
│   └─ SettingsWindow.xaml
├─ ViewModels/
│   ├─ MainViewModel.cs
│   ├─ DownloadItemViewModel.cs
│   ├─ OptionsViewModel.cs         // all option toggles bound here
│   ├─ FormatPickerViewModel.cs
│   └─ SettingsViewModel.cs
├─ Models/
│   ├─ DownloadItem.cs             // url, status, progress, log buffer
│   ├─ FormatInfo.cs               // parsed -F row
│   ├─ DownloadOptions.cs          // serializable option set
│   └─ AppSettings.cs
├─ Services/
│   ├─ IYtDlpRunner.cs / YtDlpRunner.cs
│   ├─ IFormatInspector.cs / FormatInspector.cs
│   ├─ IDownloadQueue.cs / DownloadQueue.cs
│   ├─ IBinaryResolver.cs / BinaryResolver.cs   // finds yt-dlp.exe / ffmpeg.exe
│   ├─ ISettingsStore.cs / SettingsStore.cs
│   └─ IConfImporter.cs / ConfImporter.cs       // parses yt-dlp.conf
└─ Infrastructure/
    ├─ ArgBuilder.cs               // DownloadOptions -> string[] args
    ├─ ProgressParser.cs           // parse yt-dlp progress lines
    └─ Hangul/
        └─ NfcNormalizer.cs        // post-download filename fix
```

DI: built-in `Microsoft.Extensions.DependencyInjection` configured in `App.xaml.cs`.

---

## 4. yt-dlp invocation contract

This is the most important architectural decision. **The GUI is the single source of truth for yt-dlp options.**

- **Always pass `--ignore-config`** so the user's `%APPDATA%\yt-dlp\config*` and any portable `yt-dlp.conf` next to the exe are ignored.
- **Always pass `--no-color`** and `--newline` for stable, line-buffered output suitable for parsing.
- **Always pass a structured `--progress-template`** so progress is machine-parseable (see §6).
- **Never** rely on yt-dlp's environment-detection (locale, terminal). Pass everything explicitly.

### 4.1 Conf interop (one-way import only)

A "Import from yt-dlp.conf" button in Settings:
1. Picks a `.conf` / `config.txt` file (defaults to portable location next to yt-dlp.exe).
2. Parses it with shlex-like splitting (mirror yt-dlp's `_read_user_conf`).
3. Maps known flags onto GUI options. Unknown flags listed in a "couldn't import" panel.
4. After import, GUI fully owns the settings; the conf file is not touched again.

No two-way sync, no "use conf as base" mode. Predictability beats convenience.

---

## 5. Feature list (v1)

### 5.1 Queue

- Multi-line URL input box (paste, one URL per line).
- Auto-detect URLs in clipboard (toast / banner: "3 URLs detected — Add?").
- Drag-and-drop URLs from browser.
- Queue with statuses: `Queued`, `Resolving`, `Downloading`, `Postprocessing`, `Done`, `Failed`, `Canceled`.
- Configurable parallelism (default: 2).
- Per-item: progress %, speed, ETA, current fragment count (if HLS/DASH), expandable live log.
- Per-item actions: Cancel, Retry, Open file, Open containing folder, Copy URL, Remove.
- Bulk actions on selection: Cancel all, Retry failed, Clear done.

### 5.2 Format selection

Three modes (radio):
1. **Preset** — pick from saved profiles (e.g. "1080p mp4 H.264+AAC", "Best mp4", "Audio only m4a", "Audio only MP3 320k"). Profiles editable.
2. **Inspector** — runs `yt-dlp -F --dump-single-json <url>` for the first URL in queue, shows formats in a sortable table (id / ext / resolution / fps / vcodec / acodec / filesize / tbr / note). User picks video + audio rows; tool composes `-f` string.
3. **Raw** — text box for a hand-written `-f` selector. Validated by a quick `yt-dlp --simulate -f <expr> <url>`.

### 5.3 Options panel (all exposed, grouped)

**Output**
- Output folder (browse)
- Output template (default: `%(title)s.%(ext)s`; show common placeholders as chips)
- Restrict filenames toggle
- Windows filenames toggle
- NFC filename normalization toggle (default: ON — see §8)

**Container / postprocessing**
- Merge output format (mp4/mkv/webm/auto)
- Embed metadata
- Embed thumbnail
- Embed chapters
- Write description / write info.json

**Subtitles**
- Write subs / write auto-subs / embed subs
- Languages (multi-chip input; `ko, en, en.*` etc.)
- Convert subs to (srt/vtt/none)

**Audio extraction**
- Extract audio toggle
- Codec (mp3 / m4a / opus / vorbis / wav / flac / best)
- Quality (0–9 vbr or kbps)
- Keep video toggle

**Network / auth**
- Cookies from browser (chrome/firefox/edge/brave/...) with optional profile
- Cookies file path
- Proxy (URL)
- Rate limit (e.g. 5M)
- Concurrent fragments
- Retries

**Playlist**
- Yes/No download playlist
- Playlist items (range like `1-5,8`)
- Playlist reverse / random

**Misc**
- Force overwrites
- Continue partial
- Keep video (don't delete intermediate)
- Verbose (passes `-v` for the run)
- Custom extra args (free-form text appended last)

Every option maps to a property on `DownloadOptions`; `ArgBuilder` turns the model into the final argv.

### 5.4 Format inspector

- Run with cancel button.
- Parse JSON output (`--dump-single-json`) — much more reliable than table scraping.
- Sort/filter columns.
- "Best video" / "Best audio" quick-pick buttons compose `-f bv*+ba` style selectors.

### 5.5 Logs

- Per-item rolling log (in-memory ring buffer, e.g. 5 000 lines).
- Global app log (Serilog) at `%LOCALAPPDATA%\YtDlpGui\logs\app-YYYYMMDD.log`.
- Copy log button. "Open log folder" in Settings.

### 5.6 Settings

- Path to `yt-dlp.exe` (auto-detect via `where`)
- Path to `ffmpeg.exe` (auto-detect)
- Default output folder
- Default profile
- Parallelism limit
- Theme (light / dark / system)
- Import from yt-dlp.conf (button, §4.1)
- Self-update yt-dlp button (runs `yt-dlp -U`)
- About: app version + bundled yt-dlp/ffmpeg version reported

---

## 6. Progress / output parsing

Use yt-dlp's structured progress template — never scrape the human-readable `[download] 12.3% of 45.6MiB ...` line.

```
--progress-template "PROGRESS:%(progress._percent_str)s|%(progress._downloaded_bytes)s|%(progress._total_bytes)s|%(progress._speed)s|%(progress._eta_seconds)s|%(progress.fragment_index)s|%(progress.fragment_count)s"
```

Lines starting with `PROGRESS:` are parsed by `ProgressParser` into a struct and pushed to the item's `ProgressUpdated` event. Other lines go to the item log. yt-dlp's status messages (`[download]`, `[Merger]`, `[ffmpeg]`, `[Metadata]`, `[ThumbnailsConvertor]`) are also matched to update the `Status` field.

Errors / fatal: parse `ERROR:` prefix + non-zero exit code.

---

## 7. ArgBuilder rules

- Every options group has a small builder method (`AppendOutputArgs`, `AppendFormatArgs`, ...).
- Builder produces `string[]` (not a single concatenated string) — passed via `ProcessStartInfo.ArgumentList` so quoting is handled by .NET, not by us.
- Empty / default values are *not* emitted (so the command line stays short and inspectable).
- Final command line is always logged to the per-item log on the first line for reproducibility.

Mandatory always-on prefix:
```
--ignore-config --no-color --newline --progress-template "PROGRESS:..." --no-progress
```
(`--no-progress` disables the human-readable progress bar; the template still fires.)

---

## 8. NFC filename normalization

Bake in the post-process Hangul fix that the user already validated:

- Implemented in C# (`Infrastructure/Hangul/NfcNormalizer.cs`) via `string.Normalize(NormalizationForm.FormC)` then `FormKC` fallback.
- Hooked into the download lifecycle (after yt-dlp exits with success) — does **not** rely on `yt-dlp --exec` calling out to PowerShell. The GUI knows the final file path because it has the args.
- Toggle in Output options (default ON).

---

## 9. Project layout / build

- Solution: `YtDlpGui.sln`
- Single project for v1: `src/YtDlpGui/YtDlpGui.csproj`
- Test project (for non-UI services): `tests/YtDlpGui.Tests/`
  - Cover: `ArgBuilder`, `ProgressParser`, `ConfImporter`, `NfcNormalizer`
  - xUnit
- Publish profile (`Properties/PublishProfiles/win-x64-fde.pubxml`):
  ```
  -c Release -r win-x64
  -p:PublishSingleFile=true
  -p:SelfContained=false       # user has .NET 10 runtime; smaller binary
  -p:PublishReadyToRun=true
  ```
  (Provide a `self-contained` profile too for a fully portable ~70MB build.)

---

## 10. Out of scope (v1)

- Built-in media player / preview
- Built-in clip / cut editor
- yt-dlp.exe / ffmpeg.exe bundling and self-update (only `-U` button on existing yt-dlp)
- Cloud / sync features
- Localization (English UI only; Korean later if needed)
- Cross-platform (Windows-only; Avalonia port is a v2 conversation)

---

## 11. Roadmap

**v0.1** (smoke test)
- App shell, single-URL download, hard-coded options, raw stdout in a textbox.

**v0.2**
- Queue, parallelism, per-item progress.
- ArgBuilder + DownloadOptions + binding to a basic Options panel.

**v0.3**
- Format inspector with JSON parse.
- NFC normalizer.
- Settings window + persistence.

**v0.4**
- Conf importer.
- Profile system (save/load presets).
- Polished theming (WPF-UI integration).

**v1.0**
- All §5 features done.
- Tests for core services.
- Publish profile, README with screenshots.

---

## 12. Definition of done for v1

- Can paste 10 URLs, hit Download, watch all complete with mp4/m4a outputs in chosen folder.
- Can switch to "Audio only MP3 320k" preset and re-run.
- Can open Format Inspector for a URL and pick format 137+140 manually.
- All options accessible without a CLI fallback.
- Hangul filenames come out NFC-normalized.
- App survives killing yt-dlp mid-download (cancel) and shutting down with active downloads (clean cancel + warn).
- Settings persist across runs.
- No crashes on malformed URLs / unreachable hosts / yt-dlp absent.

---

## 13. Notes for the implementer (Claude Code)

- **Don't** auto-bundle yt-dlp/ffmpeg. Resolve them at runtime.
- **Don't** hide options behind "Advanced" disclosures. This is an expert tool.
- **Do** log the exact argv used for every run on line 1 of the per-item log.
- **Do** validate raw `-f` selectors with `--simulate` before committing the run.
- **Do** prefer `ProcessStartInfo.ArgumentList` over `Arguments` to avoid Windows quoting hell.
- **Do** treat yt-dlp's stderr as a normal info channel (it writes warnings there). Only `ERROR:` prefix or non-zero exit code is a real failure.
- **Do** keep the UI thread free — all process I/O on a background task, marshal updates via `IProgress<T>` or dispatcher.
- **Do** put the NFC normalizer behind a feature flag in case the user wants raw filenames for some reason.
- Start with the v0.1 milestone and iterate. Don't try to land §5 in one PR.

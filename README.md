# YtDlpGui

A native Windows GUI front-end for [yt-dlp](https://github.com/yt-dlp/yt-dlp), built for users who already know yt-dlp's CLI but want faster batch workflows and visibility into multiple concurrent downloads.

> 한국어 README → [README.ko.md](README.ko.md)

## Goals

- Faster batch / queue workflows than typing commands
- Visibility into multiple concurrent downloads (progress, speed, ETA, log)
- Full control — every meaningful yt-dlp option is exposed in the UI, no hand-holding
- A clean, deterministic invocation contract — `--ignore-config` is always passed so the GUI is the single source of truth

This is a personal-use tool. Distribution is a non-goal for v1.

## Status

**v0.4** — Profile system, conf importer, Serilog file logging, WPF-UI theming.

See [YtDlpGui-SPEC.md](YtDlpGui-SPEC.md) for the full specification and roadmap.

## Features

- **Queue** — paste multiple URLs at once; configurable parallelism (1–8); per-item status / progress / speed / ETA / expandable log
- **Format Inspector** — `yt-dlp -F --dump-single-json` parsed into a sortable grid. Pick video + audio rows manually or use "Best video / Best audio" quick-picks; the composed `-f` selector is written back to the Options panel
- **Profile system** — four built-in presets (`1080p mp4 H.264+AAC`, `Best mp4`, `Audio only m4a`, `Audio only MP3 320k`) plus user-saved profiles
- **Conf importer** — one-way import of an existing `yt-dlp.conf`; the summary dialog shows applied flags and unrecognized flags side-by-side
- **NFC filename normalization** — automatically fixes the NFD-decomposed Hangul filenames yt-dlp produces on Windows (e.g. `ㅇㅜㅅㅏㅁㄱㅕㅂ.mp4` → `우삼겹.mp4`)
- **Settings persistence** — paths, parallelism, theme, and the last-used Options panel state are stored in `%LOCALAPPDATA%\YtDlpGui\settings.json`
- **Serilog file logging** — daily rolling log at `%LOCALAPPDATA%\YtDlpGui\logs\app-YYYYMMDD.log` (14-day retention)
- **WPF-UI theming** — System / Light / Dark, with live preview in the Settings dialog
- **Automatic app updates** — [Velopack](https://velopack.io) + GitHub Releases; a dismissible banner appears a few seconds after startup when a new version is found, and Settings > About has a manual "Check for app updates" button

## Install

Download `SLGYtDlpGui-win-Setup.exe` from [Releases](https://github.com/starlight97/SLGYtDlpGui/releases) and run it.

- No admin rights needed — it installs to `%LocalAppData%\SLGYtDlpGui`, not Program Files, so there's no UAC prompt.
- Windows SmartScreen may warn once because the build isn't code-signed. Click "More info" > "Run anyway".
- The installed app is self-contained (bundles its own .NET runtime) — nothing else to install.
- A portable `SLGYtDlpGui-win-Portable.zip` is also published for a no-install copy; it doesn't auto-update.

## Requirements

- Windows 10/11 x64
- `yt-dlp.exe` and `ffmpeg.exe` — **not bundled**; resolved at runtime
- Running from source instead of the installer: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the installed/self-contained build doesn't need this)

Binary lookup order:

1. Path explicitly set in Settings
2. Tools folder — `%LOCALAPPDATA%\YtDlpGui\bin`. Survives app updates/reinstalls, unlike the install folder. Settings > About has an "Open tools folder" button
3. Same folder as `YtDlpGui.exe`
4. Debug builds only: walking up to six directories from the exe (development convenience — drop `yt-dlp.exe` at the repo root); Release builds skip this step
5. `where.exe` lookup on `PATH`

## Updates

The installed app (via Setup.exe) checks GitHub Releases for a new version shortly after startup, and downloading + installing an update restarts the app automatically. F5 builds and unzipped/portable copies never make this network call.

If more than one YtDlpGui window is running, installing an update force-closes the other windows (Velopack replaces the whole install folder in place) — a confirmation prompt warns about this before it happens.

## Build

```bash
dotnet build YtDlpGui.sln -c Release
```

The exe lands at `src/YtDlpGui/bin/Release/net10.0-windows/YtDlpGui.exe`. This is a framework-dependent dev build (needs the .NET 10 Desktop Runtime); it does not check for app updates.

To build the self-contained Setup.exe / Portable.zip installer, see `build/pack.ps1` (requires the `vpk` tool, pinned in `build/dotnet-tools.json`).

## Project layout

```
YtDlpGui.sln
YtDlpGui-SPEC.md
src/YtDlpGui/
├─ App.xaml(.cs)             DI bootstrap, Serilog setup, theme apply
├─ Models/                   DownloadOptions, AppSettings, FormatInfo, Profile, ...
├─ Services/                 IBinaryResolver, IYtDlpRunner, IDownloadQueue,
│                            IFormatInspector, ISettingsStore, IProfileStore,
│                            IConfImporter
├─ ViewModels/               MainViewModel, OptionsViewModel, SettingsViewModel,
│                            FormatPickerViewModel, DownloadItemViewModel
├─ Views/                    MainWindow, SettingsWindow, FormatPickerWindow,
│                            ConfImportDialog, InputDialog
└─ Infrastructure/           ArgBuilder, ProgressParser, ThemeApplier,
                             Hangul/NfcNormalizer
```

Strict MVVM. No code-behind beyond `InitializeComponent` and tiny visual glue. DI via `Microsoft.Extensions.DependencyInjection`.

## Architecture notes

- The GUI always passes `--ignore-config --no-color --newline --no-progress --progress-template "PROGRESS:..."` so output is machine-parseable and the user's `yt-dlp.conf` cannot silently change behavior.
- Argv is constructed via `ArgBuilder` and dispatched through `ProcessStartInfo.ArgumentList` — no manual quoting.
- Per-item progress comes from a structured `--progress-template`, not by scraping the human-readable `[download] 12.3% of 45.6MiB` line.
- Output folder hierarchy: `Settings.OutputFolder` is authoritative across restarts; per-session edits in the Options panel are temporary.

## Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 (LTS) |
| UI | WPF + XAML |
| MVVM | CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) |
| Theming | WPF-UI (lepoco/wpfui) |
| Logging | Serilog + Serilog.Sinks.File |
| Settings | System.Text.Json |
| Process mgmt | `System.Diagnostics.Process` with async stdout/stderr |

## License

Personal-use tool. No formal license at this point.

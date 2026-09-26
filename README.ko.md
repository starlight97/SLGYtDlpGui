# YtDlpGui

[yt-dlp](https://github.com/yt-dlp/yt-dlp)의 네이티브 Windows GUI 프론트엔드. yt-dlp CLI를 이미 잘 아는 사용자가 명령어를 매번 치는 대신 배치/큐 워크플로를 빠르게 돌리고 동시 다운로드 상황을 한 눈에 보고 싶을 때 쓰는 도구.

> English README → [README.md](README.md)

## 목표

- CLI 직접 입력보다 빠른 배치 / 큐 워크플로
- 다중 동시 다운로드의 진행률 / 속도 / 예상 시간 / 로그 가시성
- 의미 있는 yt-dlp 옵션은 모두 UI로 노출 (CLI fallback 없이)
- 깔끔하고 결정론적인 호출 규약 — `--ignore-config`를 항상 넘겨서 GUI를 single source of truth로 둠

개인용 도구입니다. v1 시점까지 배포는 목표가 아닙니다.

## 현재 상태

**v0.4** — Profile 시스템, conf importer, Serilog 파일 로깅, WPF-UI 테마.

전체 스펙과 로드맵은 [YtDlpGui-SPEC.md](YtDlpGui-SPEC.md) 참고.

## 기능

- **큐** — 여러 URL 한꺼번에 붙여넣기 / 병렬도 1–8 슬라이더 / 항목별 상태 / 진행률 / 속도 / ETA / 펼칠 수 있는 로그
- **Format Inspector** — `yt-dlp -F --dump-single-json` 결과를 정렬 가능한 그리드로 표시. 비디오/오디오 행 직접 선택 또는 "Best video / Best audio" 퀵픽 → 조합한 `-f` 셀렉터를 Options 패널로 자동 주입
- **Profile 시스템** — 빌트인 프리셋 4개 (`1080p mp4 H.264+AAC`, `Best mp4`, `Audio only m4a`, `Audio only MP3 320k`) + 사용자 저장 프로필
- **Conf importer** — 기존 `yt-dlp.conf` 일방향 임포트. 요약 다이얼로그가 적용된 플래그와 매핑 못한 플래그를 좌우로 보여줌
- **NFC 파일명 정규화** — Windows에서 yt-dlp가 NFD로 분해해 저장하는 한글 파일명 자동 보정 (예: `ㅇㅜㅅㅏㅁㄱㅕㅂ.mp4` → `우삼겹.mp4`)
- **설정 영속화** — 경로, 병렬도, 테마, 마지막 Options 패널 상태가 `%LOCALAPPDATA%\YtDlpGui\settings.json`에 저장
- **Serilog 파일 로깅** — `%LOCALAPPDATA%\YtDlpGui\logs\app-YYYYMMDD.log`에 일별 롤링 (14일 보관)
- **WPF-UI 테마** — System / Light / Dark, Settings 다이얼로그에서 라이브 프리뷰
- **앱 자동 업데이트** — [Velopack](https://velopack.io) + GitHub Releases. 시작 몇 초 뒤 새 버전을 찾으면 닫을 수 있는 배너가 뜨고, Settings > About에 수동 "Check for app updates" 버튼도 있음

## 설치

[Releases](https://github.com/starlight97/SLGYtDlpGui/releases)에서 `SLGYtDlpGui-win-Setup.exe`를 받아 실행한다.

- 관리자 권한 불필요 — Program Files가 아니라 `%LocalAppData%\SLGYtDlpGui`에 설치되므로 UAC 창이 뜨지 않는다.
- 코드 서명이 안 돼 있어 Windows SmartScreen 경고가 한 번 뜰 수 있다. "추가 정보" > "실행"을 누르면 된다.
- 설치판은 self-contained(자체 .NET 런타임 포함)라 따로 설치할 게 없다.
- 설치 없이 쓰는 `SLGYtDlpGui-win-Portable.zip`도 함께 배포되지만, 이쪽은 자동 업데이트가 되지 않는다.

## 요구 사항

- Windows 10/11 x64
- `yt-dlp.exe`와 `ffmpeg.exe` — **번들 아님**, 런타임에 검색
- 설치판이 아니라 소스에서 직접 실행할 경우: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 필요 (설치판/self-contained 빌드는 불필요)

바이너리 검색 순서:

1. Settings에서 명시한 경로
2. 도구 폴더 — `%LOCALAPPDATA%\YtDlpGui\bin`. 설치 폴더와 달리 앱 업데이트/재설치에도 남는다. Settings > About의 "Open tools folder" 버튼으로 바로 열 수 있다
3. `YtDlpGui.exe`와 같은 폴더
4. Debug 빌드에서만: exe에서 최대 6단계 위까지 올라가며 검색 (개발 편의 — 레포 루트에 `yt-dlp.exe` 두면 자동 인식). Release 빌드는 이 단계를 건너뜀
5. `PATH`에서 `where.exe`로 검색

## 업데이트

설치판(Setup.exe로 설치)은 시작 직후 GitHub Releases에서 새 버전을 확인하고, 다운로드 후 설치하면 앱이 자동으로 재시작된다. F5로 실행하거나 압축 해제/포터블 상태로 실행하면 이 네트워크 호출 자체가 일어나지 않는다.

YtDlpGui 창이 여러 개 열려 있을 때 업데이트를 설치하면 나머지 창은 강제로 닫힌다(Velopack이 설치 폴더 전체를 교체하기 때문). 실행 전에 확인 창으로 미리 경고한다.

## 빌드

```bash
dotnet build YtDlpGui.sln -c Release
```

결과물: `src/YtDlpGui/bin/Release/net10.0-windows/YtDlpGui.exe`. 이건 프레임워크 종속 개발용 빌드라 .NET 10 Desktop Runtime이 필요하고, 앱 업데이트 확인도 하지 않는다.

self-contained Setup.exe / Portable.zip 설치파일을 만들려면 `build/pack.ps1`을 참고한다(`build/dotnet-tools.json`에 고정된 `vpk` 도구 필요).

## 프로젝트 구조

```
YtDlpGui.sln
YtDlpGui-SPEC.md
src/YtDlpGui/
├─ App.xaml(.cs)             DI 부트스트랩, Serilog 셋업, 테마 적용
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

엄격한 MVVM. code-behind는 `InitializeComponent`와 최소한의 visual glue뿐. DI는 `Microsoft.Extensions.DependencyInjection`.

## 아키텍처 메모

- GUI는 항상 `--ignore-config --no-color --newline --no-progress --progress-template "PROGRESS:..."`를 강제로 붙임. 출력은 기계 파싱 가능하게, 사용자의 `yt-dlp.conf`가 동작을 몰래 바꾸지 못하게.
- argv는 `ArgBuilder`로 만들고 `ProcessStartInfo.ArgumentList`로 전달 — 수동 quoting 없음.
- 항목별 진행률은 구조화된 `--progress-template`에서 파싱. 사람이 읽는 `[download] 12.3% of 45.6MiB` 라인을 스크래핑하지 않음.
- Output folder 우선순위: `Settings.OutputFolder`가 재시작 후에도 authoritative. Options 패널에서 한 세션 동안만 임시로 바꾸는 건 가능하지만 영속되지 않음.

## 스택

| 레이어 | 선택 |
|---|---|
| 런타임 | .NET 10 (LTS) |
| UI | WPF + XAML |
| MVVM | CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) |
| 테마 | WPF-UI (lepoco/wpfui) |
| 로깅 | Serilog + Serilog.Sinks.File |
| 설정 | System.Text.Json |
| 프로세스 관리 | `System.Diagnostics.Process` + async stdout/stderr |

## 라이선스

개인용 도구. 현재 공식 라이선스 미지정.

# Velopack 설치파일 + 자동업데이트 컨텍스트

## 기준점
- 베이스 커밋: `8801ad6` "[WIP] Velopack 설치파일 작업 전 스냅샷" → 그 위에 피어 `60beec1`. 사용자가 직접 push 완료 (2026-09-26, origin/main = 60beec1, 로컬과 원격 차이 없음)
- 원격: https://github.com/starlight97/SLGYtDlpGui — **public**, 릴리스 0개 → GithubSource 읽기에 토큰 불필요
- 로컬: .NET SDK 9.0.300 / 10.0.103 (8 SDK 없음, WindowsDesktop 8.0.24 런타임 팩은 캐시됨), `vpk` 미설치, Velopack NuGet 미캐시, `gh` 미설치
- 조사 결과 원본(JSON): `%TEMP%\claude\...\tasks\wr5xtzkzo.output` (워크플로우 wf_fb8343ad-90b)

## 조사 결과 요약 (2026-09-26)
- `YtDlpGui.csproj`: WinExe, net8.0-windows, UseWPF, app.manifest = **asInvoker (UAC 없음, OK)**. `<Version>` 없음(→1.0.0), 아이콘 없음(.ico 파일도 없음).
- 패키지: CommunityToolkit.Mvvm 8.4.0, MS.Extensions.DI 8.0.1, MS.Extensions.Hosting 8.0.1(미사용), Serilog 4.0.2, Serilog.Sinks.File 6.0.0, WPF-UI 3.0.5
- 기존 publish 프로필 2개(`win-x64-fde`, `win-x64-sc`) 모두 **PublishSingleFile=true** → Velopack용 신규 설정 필요
- **Program.cs/StartupObject 없음** → WPF 자동 생성 Main 사용 중 → 커스텀 Main 필요 (최대 구조 변경점)
- 설정/프로필/로그: `%LocalAppData%\YtDlpGui\` (settings.json, profiles.json, logs\) → Velopack `current` 폴더 밖이라 **업데이트에도 안전**
- yt-dlp/ffmpeg: 번들·다운로드 없음. `BinaryResolver` 탐색 순서 = 설정 경로 → **exe 옆(업데이트 시 삭제됨)** → 부모 폴더 6단계(개발용) → PATH
- 버전 표시: `Assembly.GetName().Version` (App.xaml.cs:22, SettingsViewModel.cs:57 → SettingsWindow.xaml:100), MainWindow 제목은 "v0.4" 하드코딩
- SettingsWindow "About" 섹션(~95-136)에 버전/yt-dlp -U 버튼 존재 → 앱 업데이트 버튼 자리로 적합
- MainWindow에 닫기 가능한 클립보드 배너 패턴(~155-173) → 업데이트 배너 템플릿
- 전역 예외 핸들러/단일 인스턴스/CLI 인자 처리 없음
- 경미: yt-dlp 실행 시 WorkingDirectory 미지정(YtDlpRunner.cs:26, SettingsViewModel.cs:192/217-225) — 피어 담당 파일 포함, 후속 과제

## 핵심 파일
| 파일 경로 | 역할 | 담당 |
|-----------|------|------|
| src/YtDlpGui/YtDlpGui.csproj | Velopack 패키지, `<Version>`, StartupObject, App.xaml 빌드액션 | **나** |
| src/YtDlpGui/App.xaml | (빌드액션 Page 전환, 파일 변경 최소) | **나** |
| src/YtDlpGui/Program.cs (신규) | 커스텀 Main + `VelopackApp.Build().Run()` | **나** |
| src/YtDlpGui/Services/AppUpdateService.cs (신규) | Velopack 업데이트 확인/다운로드/적용 | **나** |
| src/YtDlpGui/Views/SettingsWindow.xaml, ViewModels/SettingsViewModel.cs | "앱 업데이트 확인", bin 폴더 열기 | **나** |
| src/YtDlpGui/Services/BinaryResolver.cs | `%LocalAppData%\YtDlpGui\bin\` 후보 추가, 부모 탐색 DEBUG 전용 | **나** (시그니처 유지) |
| build/ (신규) | publish → vpk pack 스크립트 | **나** |
| src/YtDlpGui/App.xaml.cs | DI 등록 (AppUpdateService 1줄) | 🔒 피어 → "완료" 후 Phase B |
| ViewModels/MainViewModel.cs, Views/MainWindow.xaml(.cs) | 시작 시 자동 확인 배너, 다운로드 중 재시작 차단 | 🔒 피어 → "완료" 후 Phase B |
| ArgBuilder.cs, DownloadQueue.cs, DownloadOptions, YtDlpRunner.cs | 피어 작업 영역 | 🔒 피어 (건드리지 않음) |

## 핵심 결정사항
- 배포 방식: 설치 파일 + C(폴더, self-contained) + Velopack — 사용자 선택 (2026-09-26)
- 업데이트 UX: (c) 시작 시 자동 확인 + 메뉴 수동 확인 — 사용자 선택
- 업데이트 소스: GitHub Releases (public → 토큰 불필요). 릴리스 **업로드는 사용자가 직접**(토큰 필요, 외부 게시)
- 코드 서명: 범위 외
- 작업 분할: Phase A(내 파일만, 단독으로 빌드·실행 가능해야 함) → Phase B(피어 "완료" 후) → Phase C(net10 리타깃, 선택·피어 작업 종료 후)
- 설계 완료: wf_c1c30fa1-d83 → **`05-design.md`** (최종 설계서, 검토 반영). Velopack 공식 문서 검증에서 막히는 문제 없음, 코드 충돌 검토에서 major 2건 → 수정안에 반영
- 추천안 적용(사용자가 답이 없으면 추천안으로 진행하라고 함): packId `SLGYtDlpGui`, packTitle `YtDlpGui`, `<Version>0.4.0</Version>`, UI 영어, `YTDLPGUI_UPDATE_FEED` 로컬 피드 오버라이드, CWD 정규화(D11), 다중 인스턴스 가드(D12, mutex는 제외), Velopack/vpk 1.2.158 고정, Portable.zip 생성, 도구 폴더까지만(자동 다운로드 제외)
- **피어가 끝나서 Phase A+B를 합쳐 최종 형태로 한 번에 구현**: D4 임시 브리지(Shared, 선택 파라미터) 제거, DI 정식 등록, D5와 BinaryResolver DEBUG화도 함께 진행
- 사용자 승인 대기: Phase C(net10 리타깃), Setup.exe 실제 설치 E2E(사용자 PC에 설치되므로)
- 구현 워크플로우: wf_0b70d416-8b8 (core coder → ui coder → 빌드 → 리뷰 3개 + judge → fix → 최종 빌드/pack/스모크)

## 관련 클래스/함수
- `VelopackApp.Build().Run()` — Main 최상단 필수
- `UpdateManager(new GithubSource(url, null, false))` → `CheckForUpdatesAsync()` / `DownloadUpdatesAsync()` / `ApplyUpdatesAndRestart()` (정확한 시그니처는 설계 검증 단계에서 공식 문서로 확인)
- `BinaryResolver.ResolveYtDlp()` / `ResolveFfmpeg()` — 피어가 호출, 시그니처 유지
- `SettingsViewModel.SelfUpdateYtDlpAsync` — 피어의 신규 YtDlp* 서비스가 이 동작을 따름. SettingsViewModel 변경 방법은 피어가 "완료" 메시지에 전달 예정

## 의존성 / 피어 세션 조율 로그
- 피어: "YouTube 다운로드 실패" [2bf9ce] — yt-dlp 오래됨 배너 + -U 버튼 + `--ffmpeg-location`
- 2026-09-26 합의:
  - 8801ad6에는 피어 소스 변경 없음 (피어는 자체 기준점 d1f0f960 사용)
  - 파일 담당은 위 표대로. 피어 워크플로우에도 내 파일 수정 금지 규칙 적용됨
  - App.xaml.cs DI 등록 1줄은 피어 "완료" 후 내가 직접 추가 (클래스가 없으면 피어 빌드가 깨지므로)
  - 서로의 파일은 커밋하지 않음 (경로를 지정해 add)
  - 피어는 빌드/테스트를 `-c Release`로만 실행 (Debug 앱 실행 중이라 파일이 잠김) → **내 빌드는 `--artifacts-path`로 출력 폴더를 분리**해서 동시 빌드 충돌을 피한다
  - net10 리타깃은 피어 작업이 끝날 때까지 보류, 실행 전에 알림
- 2026-09-26 피어 **"완료"** 수신 → 피어 담당 파일 전부 해제, net10 리타깃도 진행 가능하다고 함. 피어 변경분은 **커밋 안 됨**(사용자 결정 대기)
  - 피어 변경: App.xaml.cs(+`services.AddSingleton<YtDlpUpdater>();` ~33행), ArgBuilder.cs, DownloadQueue.cs, MainViewModel.cs, MainWindow.xaml, MainWindow.xaml.cs
  - 피어 신규: Infrastructure/YtDlpVersionParser.cs, Services/YtDlpUpdater.cs, tests 5개 (ArgBuilderFfmpegLocation, DownloadQueueFfmpegLocation, MainViewModelYtDlpBanner, YtDlpUpdater, YtDlpVersionParser)
  - 피어 검증: `dotnet build/test -c Release` 경고 0, 테스트 124/124 통과 (net8.0-windows)
  - **Phase B 반영 사항**
    - MainWindow.xaml: 클립보드 배너 아래(~155-176)에 주황색 "yt-dlp outdated" 배너가 있음 → 앱 업데이트 배너는 **그 아래**에 둔다
    - MainWindow.xaml.cs: `Loaded += OnLoaded`(시작 시 yt-dlp 버전 확인) 추가됨. OnClosing에서 IsUpdatingYtDlp 경고를 HasActiveDownloads보다 먼저 확인
    - MainViewModel: `IsUpdatingYtDlp` 동안 AddToQueue/Retry/Validate/Inspector/OpenSettings를 막음(`CanOpenSettings` 신규) → **앱 재시작 차단 조건 = HasActiveDownloads || IsUpdatingYtDlp**
  - **SettingsViewModel 변경 방법 (피어 안내, Phase A에 포함)**
    - 생성자에 `YtDlpUpdater` 주입 (DI 싱글턴 등록돼 있음)
    - `SelfUpdateYtDlpAsync`: Resolve + RunAsync(["-U","--no-color"]) → `var r = await _updater.SelfUpdateAsync(CancellationToken.None);`, SelfUpdateStatus에 r.Message/r.Output. 예외는 서비스가 처리하므로 catch 단순화
    - `CanSelfUpdate()` → `!IsUpdatingYtDlp && !_updater.IsUpdating` (배너 Update와 Settings Self Update가 같은 중복 실행 가드 공유 → `-U` 동시 실행 경쟁 제거)
    - YtDlpUpdater는 pip으로 설치한 경우의 "Use that to update" 메시지도 실패로 처리
- 2026-09-26 피어가 **`60beec1`** "[yt-dlp] 오래된 버전 배너/업데이트 버튼 + --ffmpeg-location 전달"을 커밋함(13개 파일, push 안 함) → 모든 파일 해제. 구현 영향(BinaryResolver 순서, CWD 정규화, MainViewModel 생성자 +1, SettingsViewModel 위임, OnLoaded/OnClosing)을 피어에게 공지함
- 커밋 프로토콜 합의: 커밋하는 세션이 커밋 전에 상대에게 알림. 피어 변경분 분리 커밋은 **Phase B 수정 시작 전**에 해야 깔끔함 → 결정 전까지 피어의 6개 수정 파일은 건드리지 않음. 커밋 순서 = 피어 변경분 → 내 Phase A (SettingsViewModel이 YtDlpUpdater에 의존)

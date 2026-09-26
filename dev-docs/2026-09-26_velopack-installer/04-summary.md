# Velopack 설치파일 + 자동업데이트 완료 요약

## 날짜
시작: 2026-09-26 / 완료: 2026-09-26

## 변경 사항
- **배포 방식**: Velopack 설치 파일(`SLGYtDlpGui-win-Setup.exe`). self-contained, 단일 파일이 아닌 폴더 publish, win-x64. 사용자 폴더(`%LocalAppData%\SLGYtDlpGui`)에 설치되고 UAC가 뜨지 않는다.
- **앱 자동 업데이트**: GitHub Releases(`starlight97/SLGYtDlpGui`)를 쓴다.
  - 시작 3초 뒤 백그라운드에서 확인하고, 새 버전이 있으면 메인 배너를 띄운다. 배너를 한 번 누르면 다운로드 → 정상 종료 → 적용 → 재시작까지 진행된다.
  - Settings > About에서 수동으로 Check / Download / Restart 할 수 있다.
  - 설치본이 아니면 업데이트 UI는 조용히 비활성화되고 네트워크 호출도 하지 않는다.
- **진입점**: `Program.Main`(StartupObject)에서 `VelopackApp.Build().SetAutoApplyOnStartup(!다른창).Run()`을 호출한다.
  - CWD가 앱 폴더 안이면 `%UserProfile%`로 옮긴다(D11).
  - `App.Run()`이 반환된 뒤(OnExit 저장/flush 이후) 업데이트를 적용한다(D3).
- **다중 창 보호(D12)**: 다른 창이 열려 있으면 시작 시 자동 적용을 하지 않는다. Restart를 누르면 "다른 창이 강제 종료된다"는 경고를 띄운다.
- **yt-dlp/ffmpeg 도구 폴더**: 탐색 순서에 `%LocalAppData%\YtDlpGui\bin`을 추가했다. 이 폴더는 업데이트에도 남는다.
  - 순서: override → 도구 폴더 → exe 옆 → (DEBUG 빌드만) 부모 폴더 → PATH.
  - Settings에 "Open tools folder" 버튼을 추가했다.
- **상호 가드**: 앱 업데이트(다운로드/재시작 예약) ↔ yt-dlp `-U`가 서로 겹쳐 실행되지 않는다.
  - `YtDlpUpdater.IsUpdatingChanged`로 싱글턴 MainViewModel과 transient SettingsViewModel이 서로의 상태를 CanExecute에 반영한다.
  - 구독자 예외는 방어하고, SettingsViewModel은 창이 닫힐 때 구독을 해제한다.
- **버전 단일 소스**: csproj `<Version>0.4.0</Version>`. 창 제목은 버전에 바인딩("YtDlpGui v0.4.0")되고, pack 스크립트가 같은 값을 쓴다.
- **.NET 10 LTS 리타깃**: `net10.0-windows`.
  - 사용하지 않던 `Microsoft.Extensions.Hosting`을 제거하고 `SatelliteResourceLanguages=en;ko`로 설정했다.
  - Setup 81MB → 71MB, full nupkg 74MB → 64MB.
- **패키징 스크립트** `build/pack.ps1`(PS 5.1, ASCII):
  - csproj 버전을 읽고, NuGet과 vpk 버전이 같은지 강제한다(1.2.158, `build/dotnet-tools.json`).
  - dirty 트리 가드와 버전 역행 가드가 있다. `-Delta`는 선택이다.
  - `-Upload`/`-Publish`는 수동 전용이다(`GITHUB_TOKEN`, 클린 트리, push된 HEAD 필요).

## 수정된 파일
- `src/YtDlpGui/Program.cs` (신규): 커스텀 Main, Velopack 초기화, CWD 정규화, 종료 후 적용, 전역 크래시 처리
- `src/YtDlpGui/Services/IAppUpdateService.cs`, `AppUpdateService.cs` (신규): 상태 머신(NotInstalled/Idle/Checking/UpToDate/Available/Downloading/ReadyToRestart/Failed), 예외를 던지지 않는 계약, busy 유지(D13), 재시작 예약/취소 알림, `YTDLPGUI_UPDATE_FEED` 로컬 피드
- `src/YtDlpGui/ViewModels/AppUpdateRestart.cs` (신규): 확인과 예약, `MainWindow.Close()`를 거치는 재시작
- `src/YtDlpGui/Services/BinaryResolver.cs`: 도구 폴더 후보, `#if DEBUG` 부모 탐색, 테스트용 internal seam
- `src/YtDlpGui/Services/YtDlpUpdater.cs`: `IsUpdatingChanged` 이벤트(시작/finally), 구독자 예외 방어
- `src/YtDlpGui/ViewModels/MainViewModel.cs`, `Views/MainWindow.xaml(.cs)`: 앱 업데이트 배너(피어 yt-dlp 배너 아래), 시작 시 확인, 재시작 게이트, 제목 버전 바인딩, OnClosing 문구
- `src/YtDlpGui/ViewModels/SettingsViewModel.cs`, `Views/SettingsWindow.xaml(.cs)`: App updates 섹션, 도구 폴더, YtDlpUpdater 위임, 구독 해제
- `src/YtDlpGui/App.xaml.cs`: `IAppUpdateService` 싱글턴 DI 등록
- `src/YtDlpGui/YtDlpGui.csproj`: Velopack 1.2.158, Version, StartupObject, InternalsVisibleTo, net10, SatelliteResourceLanguages, Hosting 제거
- `build/pack.ps1`, `build/dotnet-tools.json` (신규)
- `tests/YtDlpGui.Tests/*`: 신규 테스트 6개 파일(AppEntryPoint, AppUpdateService, BinaryResolver, MainViewModel/Settings 가드, 테스트 더블), YtDlpUpdater 테스트 추가, 피어 테스트 생성자 인자, net10
- `README.md`, `README.ko.md`, `tests/**/README*.md`, `YtDlpGui-SPEC.md`, `.gitignore` 헤더: 설치/업데이트/도구 폴더/.NET 10 문서화

## 검증
- Release 빌드 경고 0, 테스트 164/164 통과(기존 124 + 신규 40)
- 설치 E2E(이 PC에서 실제로 진행하고 끝난 뒤 원상복구)
  - 조용한 설치(UAC 없음) → 첫 실행(cwd=홈) → 도구 폴더 우선 사용
  - 배너 한 번 클릭으로 0.4.0 → 0.4.1 업데이트(net8, delta 0.16MB)
  - 다중 창 가드
  - net8 0.4.1 → **net10 0.4.2** 업데이트(coreclr 10.0.326 확인)
  - 테마 전환 스모크
  - 제거(설치 폴더만 삭제, 설정 유지)
- 코드리뷰: 3개 관점 리뷰 + judge, 단계마다 code-reviewer, 최종 전체 diff 리뷰에서 확정된 문제는 모두 수정

## 배운 점 / 참고사항
- **packId는 영구적이다.** `YtDlpGui`로 했다면 설치/제거 때 기존 `%LocalAppData%\YtDlpGui`(설정)가 삭제됐을 것이다. `SLGYtDlpGui`로 분리했다.
- `ApplyUpdatesAndRestart`는 `Environment.Exit(0)`을 호출해서 OnExit 저장을 건너뛴다. 그래서 `App.Run()` 반환 뒤에 `WaitExitThenApplyUpdates`를 호출하는 방식으로 대체했다.
- 바로가기와 Update.exe 재시작은 CWD를 `current\`로 준다. 자식 프로세스가 current를 잠그지 않도록 CWD를 정규화해야 한다.
- 서브에이전트는 워크플로 스크립트에 적힌 "사용자 승인"을 검증할 수 없어서 설치/제거를 거부한다. 시스템에 영향을 주는 E2E는 사용자에게 직접 승인받은 Master가 실행했다.
- 한국어 Windows의 MessageBox 버튼은 "예(Y)/아니요(N)"이고 UIA에는 Pane으로 노출된다. `WM_COMMAND IDNO`를 PostMessage로 보내 응답했다.
- 두 인스턴스가 동시에 실행되면 Serilog가 `app-YYYYMMDD_001.log`로 따로 기록한다.
- 피어 세션("YouTube 다운로드 실패")과 파일 담당을 나누고, 커밋은 분리(60beec1)했고, 사전 공지 규칙으로 충돌 없이 병행했다.

## 후속 과제
- 첫 릴리스 게시(사용자): 커밋과 push 후 `Releases\`를 삭제하고 `$env:GITHUB_TOKEN` 설정 → `build\pack.ps1 -Upload -Publish`
- 테마 설정이 메인 화면에 반영되지 않음(기존 동작. net8에서도 같음. DynamicResource 배선은 정상이라 런타임 조사 필요)
- `AppUpdateServiceTests`의 100ms 타이밍 테스트를 더 견고하게
- 앱 아이콘(`src/YtDlpGui/Assets/app.ico`를 추가하면 csproj/pack이 자동으로 사용), 코드 서명, 첫 실행 yt-dlp/ffmpeg 자동 다운로드, 단일 인스턴스 mutex

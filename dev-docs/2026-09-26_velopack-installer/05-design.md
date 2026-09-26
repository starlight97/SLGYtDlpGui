# YtDlpGui Velopack 설치파일 + 앱 자동 업데이트 설계서 (검토 반영 최종판)

> 기준 커밋은 `8801ad6`이고, 현재 작업 트리에는 피어 WIP가 포함돼 있다. 이 설계는 **읽기 전용 조사**만으로 작성했으며 프로젝트 파일은 하나도 수정하지 않았다.
> 근거 자료:
> - Velopack/vpk **1.2.158**: 2026-09-21 공개된 stable. NuGet XML 문서, 1.2.158 태그 소스, docs.velopack.io, 검증자 리포트를 참고했다.
> - scratchpad 프로브(이번 개정에서 추가 실행):
>   - `VelopackLocator.CreateDefaultForPlatform()`을 쓰면 미설치 프로세스에서 `IsInstalled=False`가 되고, `CheckForUpdatesAsync`는 `NotInstalledException`을 던진다.
>   - `TestVelopackLocator("SLGYtDlpGui","0.4.0",tmp)`와 `SimpleFileSource`를 조합하면, 피드가 비어 있을 때 `null`, 피드에 0.5.0이 있으면 `0.5.0`을 반환한다.
>   - `TestVelopackLocator(version:null)`은 **ArgumentException을 던진다**. 이 locator로는 미설치 상태를 표현할 수 없다.
>   - `IUpdateSource` 시그니처를 확인했다.
> 각 비평 항목을 어떻게 처리했는지는 §12 "검토 반영"에 정리했다.

---

## 1. 요구사항
- Velopack `Setup.exe`로 배포한다. 빌드는 self-contained, **폴더(단일 파일 아님)**, win-x64다. 무료 도구만 쓰고(Flow 미사용) 코드 서명은 하지 않는다.
- 업데이트 UX는 두 가지다.
  1. 시작 시 백그라운드에서 확인한다. 새 버전이 있으면 화면을 막지 않는 배너를 띄우고, 사용자가 클릭하면 다운로드, 적용, 재시작까지 진행한다.
  2. Settings > About에 수동 "앱 업데이트 확인" 버튼을 둔다.
- 업데이트 소스는 GitHub Releases(`starlight97/SLGYtDlpGui`, public, 토큰 불필요)이고 `UpdateManager` + `GithubSource`를 쓴다.
- F5로 실행하거나 설치하지 않은 폴더에서 실행하면 업데이트 UI는 조용히 비활성화된다. 이때 네트워크 호출도 하지 않는다.
- 업데이트, 재설치, 제거를 거쳐도 settings/profiles/logs와 사용자가 둔 yt-dlp/ffmpeg는 남아야 한다.
- **피어 세션과 동시에 작업한다.** Phase A는 우리 파일만 수정하고, 끝난 시점에 단독으로 빌드하고 실행할 수 있어야 한다. Phase B는 피어가 "완료"를 알린 뒤 진행한다. Phase C(net10)는 선택 사항이다.
- 피어 코드가 의존하는 `BinaryResolver.ResolveYtDlp()/ResolveFfmpeg()`는 **시그니처와 의미를 모두** 호환 상태로 유지한다. 탐색 순서를 바꾸려면 피어의 명시적 ACK를 받아야 한다.

## 2. 현재 구조 (코드에서 직접 확인)
| 항목 | 현황 |
|---|---|
| 진입점 | `Program.cs`가 없다. App.xaml이 암묵적 ApplicationDefinition(`ShutdownMode=OnMainWindowClose`, StartupUri 없음)이라 WPF가 `App.Main`을 생성한다. `App.OnStartup`은 DI(`ServiceCollection`) 구성, 테마 적용, `MainWindow` 표시를 한다. `App.OnExit`는 LastOptions/Parallelism 저장, `Log.CloseAndFlush()`, provider Dispose, `base.OnExit` 순으로 동작한다. |
| DI | `SettingsViewModel`은 Transient이다. 생성 경로는 `MainViewModel.OpenSettings`에서 `_services.GetRequiredService<SettingsViewModel>()`를 호출해 `new SettingsWindow(vm){Owner=MainWindow}.ShowDialog()`로 여는 것 하나뿐이고, 직접 `new` 하는 곳은 없다. 생성자는 `(ISettingsStore, IConfImporter, OptionsViewModel, IBinaryResolver, IYtDlpRunner)`이다. |
| BinaryResolver | 공개 API는 `IBinaryResolver`의 `string? ResolveYtDlp()/ResolveFfmpeg()` 두 개다(찾지 못하면 null). 내부 `private static Resolve(exeName, overridePath)`의 순서는 override → `AppContext.BaseDirectory` → **조건 없는** 부모 6단계 → `where.exe`다. 소비자는 DownloadQueue(피어 잠금), 피어의 신규 `YtDlpUpdater`, MainViewModel이다. 실제 `BinaryResolver`를 쓰는 테스트는 없고 모두 Fake다. |
| 작업 디렉터리 | `YtDlpRunner`는 `WorkingDirectory = workingDirectory ?? string.Empty`이다. FormatInspector, YtDlpUpdater(-U, 버전 체크), MainViewModel 프로브, SettingsViewModel이 `workingDirectory: null`을 넘기므로 **프로세스 CWD를 상속**한다. DownloadQueue만 `item.Options.OutputFolder`를 넘긴다. |
| 데이터 경로 | `%LocalAppData%\YtDlpGui\` 아래에 settings.json과 logs\가 있다(이 PC에 실제로 있음). `bin\`은 **없다**. 리터럴 "YtDlpGui"가 SettingsStore, ProfileStore, App.xaml.cs 세 곳에 중복돼 있다. |
| 버전 | `<Version>`이 없어서 1.0.0이다. 화면 표시는 `Assembly.GetName().Version.ToString(3)`이고, MainWindow 제목에는 "YtDlpGui v0.4"가 하드코딩돼 있다. |
| publish 프로필 | pubxml 두 개가 모두 `PublishSingleFile=true`인데, `*.pubxml`이 gitignore 대상이라 **추적되지 않는 로컬 파일**이다. |
| .gitignore | `Releases/`, `artifacts/`, `publish/`, `*.exe`, `*.pubxml`을 무시한다. `build/`는 무시하지 않는다. |
| InternalsVisibleTo | 없다. 테스트는 public 타입만 사용한다. |
| 스타일 | ViewModel은 `ObservableObject` + `[ObservableProperty]` + `[RelayCommand(CanExecute=...)]`를 쓴다. UI 마샬링은 `Application.Current?.Dispatcher.BeginInvoke(() => ...)`, 이벤트는 `event Action<...>?`, 로깅은 정적 Serilog `Log.Information/Warning(ex, ...)`, 동시 실행 가드는 `Interlocked.CompareExchange`이다. VM에서 `MessageBox.Show(ActiveOwner(), ...)`를 직접 호출하는 패턴이 이미 있다(SettingsViewModel, OptionsViewModel). 인터페이스 파일에 관련 record/enum을 같이 둔다(`IYtDlpRunner.cs`). 주석과 UI 문자열은 영어다. WPF 프로젝트라서 `System.IO`와 `System.Diagnostics`는 implicit using에 없으므로 **파일마다 명시**한다. |
| 피어 WIP | App.xaml.cs(`AddSingleton<YtDlpUpdater>()`), ArgBuilder, DownloadQueue, MainViewModel(+197줄), MainWindow.xaml(yt-dlp 배너), MainWindow.xaml.cs(`OnLoaded`에서 `_ = vm.CheckYtDlpVersionAsync()`, `OnClosing`에서 `IsUpdatingYtDlp` 확인창 → active downloads 확인창 순), 신규 `YtDlpUpdater.cs`, `YtDlpVersionParser.cs`와 테스트 5개. 그중 `MainViewModelYtDlpBannerTests.cs:35`가 `new MainViewModel(...)`을 **인자 7개**로 호출한다. |

## 3. 핵심 설계 결정

### D1. packId = `SLGYtDlpGui` (`YtDlpGui`는 기각. 문서와 소스로 확정)
- 설치 루트 `%LocalAppData%\{packId}\` 구성: `current\`(앱 + sq.version), `Update.exe`, `packages\`, 루트 실행 스텁 `{packTitle}.exe`.
- 업데이트 때는 `current\`만 교체된다.
- **설치/제거 때는 `{packId}` 폴더 전체를 교체하거나 삭제한다**(docs: "During install/uninstall, the entire {packId} folder is replaced or removed").
  - Setup은 비어 있지 않은 루트를 발견하면 덮어쓰기/복구 대화상자를 띄운다. 사용자가 수락하면 루트를 롤백 폴더로 이름을 바꾼 뒤 설치에 성공하면 삭제한다(`install.rs`).
  - 제거 시에는 `remove_dir_contents(root)`가 실행된다(`uninstall.rs`).
- 따라서 packId를 `YtDlpGui`로 하면 기존 `%LocalAppData%\YtDlpGui`(설정, 로그, 앞으로의 bin)가 첫 설치와 제거 때 **삭제된다**. `SLGYtDlpGui`로 하면 설치 루트와 데이터 루트가 분리돼 안전하다. 문서에도 "설치 폴더 밖의 데이터는 제거 후에도 남는다"고 나와 있다.
- 사용자에게 보이는 이름은 `--packTitle YtDlpGui`로 정한다. 바로가기는 `YtDlpGui.lnk`, 루트 스텁은 `YtDlpGui.exe`, AUMID는 `velopack.SLGYtDlpGui`가 된다.
- 형식 규칙 `^[\w\.-]*$`를 만족한다. 문서가 권장하는 `<Company>.<App>` 형식(`SLGarden.YtDlpGui`)도 가능하므로 사용자 결정 항목으로 남긴다.
- **packId는 영구적이다.** 한 번 배포한 뒤 바꾸면 기존 설치본의 업데이트 체인이 끊긴다.

### D2. 진입점: `Program.cs` + `<StartupObject>YtDlpGui.Program</StartupObject>`, App.xaml은 ApplicationDefinition 그대로 (공식 가이드와 의도적으로 다름)
- 공식 WPF 가이드와 샘플은 `<ApplicationDefinition Remove="App.xaml"/><Page Include="App.xaml"/>` + StartupObject 방식이다. 이 설계는 **의도적으로 App.xaml을 ApplicationDefinition으로 두고 StartupObject만 지정한다.**
- 채택 근거:
  - ① 검증자의 scratch 빌드(SDK 10.0.103)에서 경고 0, 오류 0, CS0017이 나지 않았다. 생성된 `App.Main`은 무시되고, `EntryPoint`는 Program이며, public `App.InitializeComponent()`가 XAML의 ShutdownMode를 적용했다.
  - ② vpk의 VelopackApp 검사는 `ManagedEntryPointMethod`(= Program.Main)에서 `VelopackApp::Run` 호출을 찾는다. 따라서 통과한다(pack 로그 "Verified VelopackApp.Run() in …Program::Main").
  - ③ csproj에 item 조작을 추가할 필요가 없어 변경이 한 줄이다. XAML 디자이너와 Application 리소스 해석도 지금과 똑같다.
- **Page 전환도 똑같이 유효한 대안이다.** 초안에서 기각 사유로 든 "Page glob 중복 위험"은 틀린 주장이라 철회한다(공식 샘플이 바로 그 스니펫을 쓴다).
- 한쪽만 적용하면 빌드가 깨진다. Program.cs만 있으면 CS0017, StartupObject만 있으면 CS1555가 나므로 두 변경은 **연달아** 적용한다.
- 검증: 테스트에서 `Assembly.EntryPoint.DeclaringType == YtDlpGui.Program`을 확인하고, pack 로그에서 위 검사 통과 메시지를 확인한다.

### D3. 적용과 재시작은 "Program.Main 후처리 핸드오프"로 한다
- **`ApplyUpdatesAndRestart`는 기각한다.** 소스상 `WaitExitThenApplyUpdates(...)` 직후 `Exit(0)`을 호출하므로, `App.OnExit`(설정 저장, Serilog flush)가 실행되지 않는다.
- **`WaitExitThenApplyUpdates` + `Application.Shutdown()`도 기각한다.** Update.exe가 대기를 먼저 시작해 버려서, 사용자가 OnClosing 확인창에 답하기 전에 60초 제한 시계가 흐른다.
- **채택한 흐름:**
  1. 다른 인스턴스가 있으면 경고한다(D12).
  2. `ScheduleApplyOnExit()`로 정적 핸드오프를 설정한다.
  3. `MainWindow.Close()`로 정상 종료 경로를 탄다. 이때 피어의 `IsUpdatingYtDlp` 확인창과 active downloads 확인창이 그대로 동작하고, 사용자가 취소할 수 있다.
  4. Close 후에도 창이 보이면 사용자가 "No"를 누른 것이므로 `CancelScheduledApply()`를 호출한다.
  5. 정상 종료라면 `App.Run()`이 반환된다. 이 시점에는 OnExit의 저장과 flush가 끝났다. Program.Main이 `WaitExitThenApplyUpdates(asset, silent:false, restart:true)`를 호출하고 반환한다.
  6. Update.exe가 PID 종료를 기다렸다가 적용하고 재시작한다.
- **60초의 정확한 의미**(소스 기준, 정정): Update.exe는 최대 60초 기다린 뒤 *포기하지 않는다*. 경고만 남기고 계속 진행해 설치 루트 아래 프로세스를 강제 종료한 다음 적용한다. 이 흐름에서는 핸드오프가 OnExit 이후에만 일어나므로, 강제 종료되더라도 이미 저장이 끝난 프로세스의 잔여 스레드만 대상이 된다. 따라서 무해하다.
- 핸드오프 호출이 예외로 실패하면, 다음 실행 때 `VelopackApp`의 auto-apply(기본 ON)가 적용한다. 단 D12 조건에서는 auto-apply가 꺼진다.

### D4. Phase A의 DI 문제: 선택적 생성자 파라미터 + 프로세스 공유 인스턴스(임시 브리지)
- App.xaml.cs가 잠겨 있으므로 `SettingsViewModel(..., IAppUpdateService? appUpdates = null)`로 선언한다. MS DI 8.0.1은 등록되지 않은 선택적 파라미터에 기본값을 넣는다(`ValidateOnBuild=true` 조건에서 프로브로 확인). 유일한 생성 경로가 DI이므로 안전하다.
- null이면 `AppUpdateService.Shared`(Lazy, 프로세스당 1개)를 쓴다. Settings를 다시 열어도 진행 중인 다운로드 상태가 보이고, 두 번째 다운로드가 Velopack 전역 lock과 충돌하지 않는다.
- Settings를 처음 열 때 생성되므로 `VelopackApp.Run()`은 이미 실행된 상태다. locator가 설정돼 있어 생성자가 예외를 던지지 않는다.
- Phase B에서는 DI singleton으로 등록한 뒤 `Shared`를 삭제하고 파라미터를 필수로 바꾼다.
- 테스트용으로 `internal AppUpdateService(IUpdateSource? source, IVelopackLocator? locator, TimeSpan? checkTimeout = null)` 생성자를 둔다. csproj에 `InternalsVisibleTo`를 추가한다. MS DI는 public 생성자만 보므로 DI 동작에 영향이 없다.

### D5. 이벤트 구독 해제는 `SettingsWindow.Closed`에서 `vm.OnWindowClosed()`를 호출해 처리한다 (피어 ACK 필수)
- 서비스가 transient VM보다 오래 살기 때문에, 구독을 해제하지 않으면 다이얼로그를 열 때마다 VM이 하나씩 남는다.
- `SettingsWindow.xaml.cs`는 "우리 파일"과 "잠금" 목록 어디에도 없다. 그래서 **피어의 명시적 ACK를 Phase A 게이트 조건**으로 둔다.
- ACK를 받지 못하면 이 한 줄은 Phase B로 미룬다. 그 동안 남는 VM은 무해하다. 크기가 작고, 받는 이벤트는 죽은 VM 속성 갱신뿐이다.
- 대안인 WeakEventManager(리플렉션 기반)와 WeakReferenceMessenger(코드베이스에 없는 새 패턴)는 기각한다.

### D6. 버전 단일 소스: csproj `<Version>0.4.0</Version>`
- `build/pack.ps1`이 `dotnet msbuild -getProperty:Version`으로 읽어서 `--packVersion`으로 넘긴다. 3자리 SemVer2 형식이어야 하고, 4자리는 vpk가 거부한다.
- About 화면에는 "0.4.0"이 표시된다. MainWindow 제목의 v0.4와도 맞는다. app.manifest의 0.1.0.0은 기능과 무관하므로 그대로 둔다.

### D7. yt-dlp/ffmpeg: 업데이트에도 남는 도구 폴더 `%LocalAppData%\YtDlpGui\bin\`
- 경로는 `ISettingsStore.SettingsFilePath`의 디렉터리 + "bin"으로 만든다. 새 리터럴을 추가하지 않는다. 공개 static 헬퍼 `BinaryResolver.GetManagedBinDirectory(ISettingsStore)`를 두고, `IBinaryResolver`는 **변경하지 않는다**.
- **Phase A:** override 다음, exe 옆보다 앞에 tools 폴더 후보를 **추가만** 한다. 이 폴더는 현재 이 PC에 없고 앱이 파일을 자동으로 넣지도 않는다. 따라서 사용자가 직접 exe를 넣기 전까지는 **모든 소비자의 결과가 바이트 단위로 같다.** 부모 폴더 탐색은 Phase A에서 **그대로 둔다.**
- **Phase B**(피어 완료 후, 피어 ACK): 부모 6단계 탐색을 `#if DEBUG` 전용으로 바꾼다. 설치판은 `…\SLGYtDlpGui\current`에서 위로 올라가며 `%LocalAppData%`, `C:\Users\<user>`까지 뒤지므로, 거기 떠도는 오래된 yt-dlp.exe가 PATH보다 먼저 잡힐 수 있다. 이는 바로 피어가 고치고 있는 "stale yt-dlp" 문제와 같은 종류다.
- 테스트 가능하도록 `Resolve`를 `internal static Resolve(exeName, overridePath, managedBinDir, baseDirectory)`로 바꾸고, 새 우선순위를 테스트로 고정해 피어가 확인할 수 있게 한다.
- Settings에 "Open tools folder" 버튼을 추가한다. SeedConfDirectory의 자체 부모 탐색은 `_binaries.ResolveYtDlp()`의 디렉터리로 바꾼다. 우리 파일이고, 파일 선택 대화상자의 초기 폴더에만 쓰인다.
- 첫 실행 시 자동 다운로드는 범위 밖(후속 과제)이다.

### D8. 도구와 빌드 격리
- vpk는 `build/dotnet-tools.json` 로컬 매니페스트에 1.2.158로 고정한다. `.config` 밖에 있어도 해당 디렉터리에서 탐색된다(검증자 확인).
- pack.ps1이 NuGet 버전과 vpk 버전이 같은지 **강제**한다. vpk 자체는 불일치를 경고만 한다.
- publish와 검증 빌드는 `--artifacts-path`로 obj/bin을 `src\` 밖으로 격리해, 피어의 동시 빌드와 project.assets.json이 부딪히지 않게 한다.
- 새 pubxml은 만들지 않는다. `*.pubxml`이 git에서 무시되므로 스크립트 플래그를 유일한 정의로 삼는다.

### D9. UI 문자열은 영어 (사용자 결정 항목)
기존 UI가 전부 영어이므로 "앱 업데이트 확인"은 "Check for app updates"가 된다.

### D10. E2E용 피드 오버라이드 `YTDLPGUI_UPDATE_FEED` (검증 완료)
- 값이 있으면 `new UpdateManager(path, null, locator)`로 `SimpleFileSource`를 쓴다. `releases.{channel}.json`을 읽고 없으면 `*.nupkg`를 스캔한다. 오버라이드가 활성화되면 Warning 로그를 남긴다.
- GitHub에 게시하지 않고 로컬 `Releases\`만으로 전체 흐름을 검증할 수 있다.

### D11. [신규] 작업 디렉터리 정규화 (Program.Main)
- 바로가기와 Update.exe 재시작은 모두 CWD를 `{root}\current`로 해서 앱을 띄운다(`shortcuts.rs`, `start_package`).
- `workingDirectory:null`로 실행되는 yt-dlp 자식 프로세스(포맷 조회, `-U`, 버전 프로브)가 이 CWD를 상속한다. 그러면 두 가지 문제가 생긴다.
  - ① 문서가 lock 원인으로 명시한 "CWD가 current 안에 있는 프로세스"가 된다. 이 프로세스들은 루트 밖 exe라 `force_stop_package`로도 종료되지 않아 current 이름 변경을 막는다.
  - ② 상대 경로(빈 OutputFolder, conf의 상대 `-P` 등)가 업데이트 때마다 지워지는 current\ 아래로 해석된다.
- 해결: `Run()` 직후, **CWD가 `AppContext.BaseDirectory`와 같거나 그 하위일 때만** `%UserProfile%`로 옮긴다. 명령줄에서 다른 폴더를 CWD로 지정해 실행한 경우는 그대로 둔다. 판정은 `internal static GetWorkingDirectoryOverride(cwd, appDir, home)` 순수 함수로 분리해 테스트한다.
- 피어 코드(YtDlpRunner)는 수정하지 않는다. 자식 프로세스 CWD가 바뀌는 것은 의미 변경이므로 피어 ACK 항목에 넣는다.

### D12. [신규] 다중 인스턴스 보호
- Velopack의 apply 단계는 설치 루트에서 실행 중인 **다른 모든 프로세스를 OnClosing 없이 강제 종료한다**(`_force_stop_package`). 이는 Restart to update, 시작 시 auto-apply, Setup 재실행 모두에 해당한다. 단일 인스턴스 가드가 없으므로, 다운로드 중인 두 번째 창이 조용히 죽을 수 있다.
- `AppUpdateService.IsAnotherInstanceRunningCore()`: 같은 프로세스 이름이면서 **exe 전체 경로가 같은** 다른 프로세스가 있는지 검사한다. 루트 스텁은 경로가 다르므로 제외된다. 경로를 읽을 수 없는 프로세스는 보수적으로 "있음"으로 친다.
  - ① Program.Main: `VelopackApp.Build().SetAutoApplyOnStartup(!anotherInstance).Run()`. 다른 창이 열려 있으면 시작 시 auto-apply를 하지 않는다. 받아 둔 패키지는 `UpdatePendingRestart`로 남으므로, 서비스가 시작할 때 `ReadyToRestart` 상태로 복원한다.
  - ② Restart to update(Phase A는 Settings, Phase B는 배너): 다른 창이 있으면 "그 창은 즉시 닫히고 다운로드가 취소된다"는 확인창을 띄운다.
- Run() 전에 프로세스 목록을 조회하므로 "Run()이 Main의 첫 코드여야 한다"는 권고에서 조금 벗어난다. 부작용이 없고 수 ms 걸리는 조회라 허용한다. 훅 호출(install/uninstall)에도 영향이 없다.
- 단일 인스턴스 mutex는 후속 과제다.

### D13. [신규] CheckAsync 동시성: 버려진 호출이 끝날 때까지 busy 유지
- `CheckForUpdatesAsync()`에는 CancellationToken이 없다. 그래서 60초 타임아웃과 호출자 취소는 *대기만* 끊고, Velopack 호출 자체는 계속 실행된다.
- 초안은 이때 finally에서 `_busy`를 풀어 버렸다. 그러면 재시도할 때 **두 번째 CheckForUpdatesAsync가 겹쳐 실행되고**, 나중에 끝난 쪽이 `_pending`/`_status`를 덮어쓴다(비평 C1).
- 수정: 타임아웃이나 취소로 대기를 끊었는데 내부 Task가 아직 끝나지 않았다면, finally에서 busy를 풀지 않는다. 대신 그 Task의 continuation에서 풀고, 예외는 관찰해 로그로 남긴다. 늦게 도착한 결과는 버린다.
- 그 동안의 재시도는 현재 상태를 그대로 반환한다(no-op). 타임아웃 문구로 "잠시 후 다시 시도"를 안내한다.
- MainViewModel의 `_versionCheckSeq` 같은 세대 카운터 방식만으로는 "덮어쓰기"는 막아도 **Velopack 호출이 겹치는 것**은 막지 못한다. 그래서 busy 유지 방식을 택했다. 늦은 결과를 폐기하는 것이 사실상 세대 비교 역할을 한다.
- `DownloadUpdatesAsync`는 CT를 존중하고, `await Task.Run(...)`이 실제 내부 Task를 기다리므로 finally에서 풀어도 안전하다.

---

## 4. 상태와 사용자에게 보이는 화면
| AppUpdateState | Settings > About (Phase A) | 메인 배너 (Phase B) |
|---|---|---|
| NotInstalled | "Automatic updates work only in the installed app (Setup.exe). This copy runs from a build/unzipped folder." 버튼 비활성 | 표시 안 함. 네트워크 호출 없음 |
| Idle | "Current version 0.4.0." + [Check for app updates] | 없음 |
| Checking | "Checking GitHub for a new version…" | 없음 (백그라운드) |
| UpToDate | "You're up to date (0.4.0)." | 없음 |
| Available | "Version 0.5.0 is available (current 0.4.0)." + [Download update] | "YtDlpGui 0.5.0 is available." [Update & restart] [✕] |
| Downloading | "Downloading 0.5.0… 42%" + ProgressBar | 같은 문구 + 얇은 ProgressBar. ✕는 숨기기만 하고 다운로드는 계속 |
| ReadyToRestart | "Version 0.5.0 is ready. Restart to finish updating — otherwise it is applied the next time YtDlpGui starts." + [Restart to update] | "YtDlpGui 0.5.0 is ready." [Restart now] [✕]. 이전 세션에서 받아 둔 패키지는 시작 즉시 표시 |
| Failed | "App update failed: {message}" (Check로 재시도). 타임아웃은 "Timed out waiting for GitHub. Try again in a minute." | 시작 시 자동 확인 실패는 로그만 남김. 배너 버튼을 누른 뒤 실패하면 "App update failed — see Settings > About" [✕] |

재시작 경로에서는 피어의 OnClosing 확인창(yt-dlp -U 진행 중, active downloads)이 그대로 뜬다. 하나라도 "No"면 앱은 유지되고 ReadyToRestart도 유지된다. Phase B에서는 active downloads 문구를 "Cancel them and restart to install the update?"로 바꾼다.

## 5. 스레드, 에러, 취소 정책
- **서비스는 WPF를 모른다.** 내부 await는 모두 `ConfigureAwait(false)`이고, Velopack 호출은 `Task.Run`으로 감싼다.
- `StatusChanged`는 임의 스레드에서 발생한다. VM은 `Dispatcher.BeginInvoke(() => Apply(_appUpdates.Status))`로 **최신 스냅샷을 다시 읽어** 반영하므로 순서가 꼬여도 안전하다.
- 상태는 불변 record를 `volatile` 참조로 통째로 교체한다. 동시 실행은 `Interlocked` busy로 막는다(D13).
- 서비스 메서드는 **예외를 던지지 않는다.** 실패는 `Log.Warning(ex, …)` + Failed 상태로 처리한다. `NotInstalledException`은 `IsInstalled` 가드로 원천 차단한다.
- 생성자 실패는 삼킨다(Run() 없이 생성하면 `InvalidOperationException "No VelopackLocator has been set"`). 이때 `_manager=null`이 되어 NotInstalled처럼 동작한다.
- 핸드오프 실패는 Serilog가 이미 닫힌 뒤라 `Debug.WriteLine`만 남긴다. 적용은 Velopack 자체 로그 `%LocalAppData%\velopack\velopack_SLGYtDlpGui.log`로 추적한다.
- Program.Main에서 처리되지 않은 예외가 올라오면 `Log.Fatal` + `CloseAndFlush` + MessageBox를 띄운다(공식 WPF 샘플과 같은 패턴). 지금까지는 WER 크래시였으므로 동작이 바뀌지만 개선이다.

---

## 6. Phase A — 우리 파일만 수정 (끝나면 단독 빌드/실행 가능)

### 6.0 게이트와 피어 공지 (Master가 전달)
**[ACK 필수]** 피어가 답하기 전에는 해당 파일을 수정하지 않는다.
1. **BinaryResolver 탐색 순서**: override → **`%LocalAppData%\YtDlpGui\bin` (신규)** → exe 옆 → 부모 6단계(변경 없음) → PATH 순이 된다. 공개 시그니처와 null 의미는 그대로다. 이 폴더는 지금 없으므로 피어의 현재 결과는 그대로다. 우선순위는 `BinaryResolverTests`로 고정한다. 부모 탐색을 DEBUG 전용으로 바꾸는 것은 Phase B에서 별도로 ACK를 받는다.
   - 거부될 경우: BinaryResolver와 tools 폴더 UI를 Phase B로 미룬다.
2. **작업 디렉터리**: 설치판과 F5에서 앱 CWD가 exe 폴더이면 `%UserProfile%`로 옮긴다. `workingDirectory:null`로 띄우는 yt-dlp(-U, 버전 체크, 포맷 조회)의 CWD가 바뀐다. 절대 경로만 쓰는 피어 기능에는 영향이 없을 것으로 본다.
   - 거부될 경우: D11을 Phase B로 미루고, 리스크로 기록한다.
3. **SettingsWindow.xaml.cs 한 줄**(`Closed += …`).
   - 거부되거나 답이 없을 경우: Phase B로 미룬다(D5).

**[타이밍 조율]**
4. csproj 변경(Velopack PackageReference로 restore 발생, StartupObject와 Program.cs로 진입점 교체, InternalsVisibleTo, Version 0.4.0). 피어가 빌드 중이면 끝날 때까지 기다렸다가 적용한다.

**[공지]**
5. tests에 `AppEntryPointTests.cs`, `BinaryResolverTests.cs`, `AppUpdateServiceTests.cs`, `AppUpdateTestDoubles.cs`를 추가한다.
6. SettingsViewModel의 `SelfUpdateYtDlpAsync`는 건드리지 않고 피어의 안내를 기다린다.
7. Phase B 예고: App.xaml.cs, MainViewModel(생성자 8번째 인자), MainWindow.xaml(.cs), **`MainViewModelYtDlpBannerTests.cs`(생성자 인자 1개 추가)**, BinaryResolver 부모 탐색 DEBUG화.

**작업 순서**(피어가 동시에 빌드하므로 트리가 깨지는 시간을 최소화):
1. csproj(StartupObject 제외)
2. IAppUpdateService/AppUpdateService
3. **Program.cs를 만들고 곧바로 csproj에 StartupObject 추가**
4. BinaryResolver
5. AppUpdateRestart, SettingsViewModel, XAML, (ACK 후) .xaml.cs
6. tests
7. build/
8. dev-docs

### 6.1 `src/YtDlpGui/YtDlpGui.csproj`
```xml
  <PropertyGroup>
    ... (기존 유지)
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <!-- Custom entry point so VelopackApp runs before WPF (Program.cs). App.xaml intentionally stays
         ApplicationDefinition (verified: no CS0017, passes vpk's VelopackApp check). -->
    <StartupObject>YtDlpGui.Program</StartupObject>
    <!-- Single source of truth: Settings > About and build/pack.ps1 (vpk --packVersion). 3-part SemVer2. -->
    <Version>0.4.0</Version>
  </PropertyGroup>

  <!-- Drop Assets\app.ico in later: exe/shortcut icon pick it up and pack.ps1 passes it to vpk --icon. -->
  <PropertyGroup Condition="Exists('Assets\app.ico')">
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    ... (기존 6개 유지 — Hosting 제거는 Phase C)
    <!-- Must equal the vpk version in build/dotnet-tools.json (pack.ps1 enforces). -->
    <PackageReference Include="Velopack" Version="1.2.158" />
  </ItemGroup>

  <ItemGroup>
    <!-- AppUpdateService / BinaryResolver / Program test seams. -->
    <InternalsVisibleTo Include="YtDlpGui.Tests" />
  </ItemGroup>
```
Velopack 1.2.158은 `lib/net8.0`, `net9.0`, `net10.0`을 포함하고 net8+ 의존성이 없다. RuntimeIdentifier와 SelfContained는 csproj에 넣지 않는다. 넣으면 개발 출력 경로가 바뀌어 피어에게 영향이 가므로 pack.ps1 CLI로만 지정한다.

### 6.2 `src/YtDlpGui/Program.cs` (신규)
```csharp
using System.IO;
using System.Windows;
using Serilog;
using Velopack;
using YtDlpGui.Services;

namespace YtDlpGui;

/// <summary>
/// Custom entry point (csproj StartupObject). VelopackApp must run before any WPF code: when
/// Setup/Update.exe launch the app with hook arguments (install/update/uninstall) Run() handles
/// them and exits. It also auto-applies a previously downloaded update on startup.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        try
        {
            // Applying an update force-kills every process started from the install folder (no Closing
            // prompt), so never auto-apply while another YtDlpGui window — maybe mid-download — is open.
            var anotherInstance = AppUpdateService.IsAnotherInstanceRunningCore();
            VelopackApp.Build()
                .SetAutoApplyOnStartup(!anotherInstance)
                .Run();

            LeaveAppFolder();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            // App.OnExit didn't run on this path, so flush whatever Serilog buffered.
            Log.Fatal(ex, "Unhandled exception");
            Log.CloseAndFlush();
            MessageBox.Show($"YtDlpGui crashed:\n{ex.Message}\n\nDetails are in the log folder.",
                "YtDlpGui", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // App.OnExit has saved settings and flushed logs; hand off to Update.exe if "Restart to update" was chosen.
        AppUpdateService.ApplyScheduledUpdateOnExit();
    }

    /// <summary>
    /// Shortcuts and Update.exe start us with CWD = …\current, the folder Velopack replaces on every
    /// update. yt-dlp children started with an empty WorkingDirectory inherit it (blocking the swap,
    /// and resolving relative paths into a folder that gets wiped), so move to the user profile.
    /// </summary>
    private static void LeaveAppFolder()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(home)) return;
            var target = GetWorkingDirectoryOverride(Environment.CurrentDirectory, AppContext.BaseDirectory, home);
            if (target is not null) Environment.CurrentDirectory = target;
        }
        catch
        {
            // Keep the inherited directory; only relative paths and update swaps are affected.
        }
    }

    /// <summary>Returns <paramref name="home"/> when <paramref name="cwd"/> is the app folder or inside it; otherwise null.</summary>
    internal static string? GetWorkingDirectoryOverride(string cwd, string appDir, string home)
    {
        var app = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cwd));
        var inside = current.Equals(app, StringComparison.OrdinalIgnoreCase)
            || current.StartsWith(app + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return inside ? home : null;
    }
}
```

### 6.3 `src/YtDlpGui/Services/IAppUpdateService.cs` (신규. `IYtDlpRunner.cs`처럼 enum과 record를 같은 파일에 둔다)
```csharp
namespace YtDlpGui.Services;

public enum AppUpdateState
{
    /// <summary>Not a Velopack install (F5 / unzipped build): every operation is a no-op.</summary>
    NotInstalled,
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    /// <summary>Downloaded; applied by "Restart to update" or automatically on the next launch.</summary>
    ReadyToRestart,
    Failed,
}

/// <summary>Immutable snapshot, replaced wholesale on every change so any thread sees a consistent view.</summary>
public sealed record AppUpdateStatus(
    AppUpdateState State,
    string CurrentVersion,
    string? AvailableVersion = null,
    int Progress = 0,
    string? Error = null);

/// <summary>App self-update via Velopack + GitHub Releases (separate from yt-dlp's own -U).</summary>
public interface IAppUpdateService
{
    bool IsInstalled { get; }
    AppUpdateStatus Status { get; }
    bool IsApplyScheduled { get; }

    /// <summary>Raised on an arbitrary thread; marshal to the UI thread before touching bound properties.</summary>
    event Action<AppUpdateStatus>? StatusChanged;

    /// <summary>Never throws (failures → Failed + log). Returns the current status if a Velopack call is already running.</summary>
    Task<AppUpdateStatus> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads the release found by the last CheckAsync. Never throws.</summary>
    Task<AppUpdateStatus> DownloadAsync(CancellationToken ct = default);

    /// <summary>Arms the hand-off Program.Main runs after the WPF app exits. False unless ReadyToRestart.</summary>
    bool ScheduleApplyOnExit();

    void CancelScheduledApply();

    /// <summary>True if another process runs this same exe — applying an update would force-kill it.</summary>
    bool IsAnotherInstanceRunning();
}
```

### 6.4 `src/YtDlpGui/Services/AppUpdateService.cs` (신규)
```csharp
using System.Diagnostics;
using Serilog;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace YtDlpGui.Services;

public sealed class AppUpdateService : IAppUpdateService
{
    /// <summary>Keep in sync with $RepoUrl in build/pack.ps1.</summary>
    private const string GithubRepoUrl = "https://github.com/starlight97/SLGYtDlpGui";

    /// <summary>Test hook: point at a local vpk output folder (…\Releases) to exercise the flow without publishing.</summary>
    private const string FeedOverrideVariable = "YTDLPGUI_UPDATE_FEED";

    private static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromSeconds(60);

    // Phase A bridge — App.xaml.cs can't register us yet. One process-wide instance so reopening Settings
    // sees an in-flight download instead of starting a second one. Removed in Phase B.
    private static readonly Lazy<AppUpdateService> SharedInstance = new(() => new AppUpdateService());
    internal static AppUpdateService Shared => SharedInstance.Value;

    // Process-lifetime hand-off to Program.Main, consumed after App.Run() returns.
    private static volatile ScheduledApply? _scheduled;
    private sealed record ScheduledApply(UpdateManager Manager, VelopackAsset Asset);

    private readonly UpdateManager? _manager;
    private readonly TimeSpan _checkTimeout;
    private UpdateInfo? _pending;          // result of the last check that found an update
    private VelopackAsset? _readyAsset;    // downloaded (this or an earlier session), not applied yet
    private int _busy;                     // 1 while any Velopack call runs — including an abandoned check
    private volatile AppUpdateStatus _status;

    public AppUpdateService() : this(source: null, locator: null) { }

    /// <summary>
    /// Test seam: VelopackApp.Run() never executes in the test host, so pass a locator explicitly
    /// (VelopackLocator.CreateDefaultForPlatform() = not installed; TestVelopackLocator = installed).
    /// </summary>
    internal AppUpdateService(IUpdateSource? source, IVelopackLocator? locator, TimeSpan? checkTimeout = null)
    {
        _checkTimeout = checkTimeout ?? DefaultCheckTimeout;
        try
        {
            _manager = CreateManager(source, locator);
            IsInstalled = _manager.IsInstalled;
            // Left over when startup auto-apply was skipped (another instance) or failed.
            if (IsInstalled) _readyAsset = _manager.UpdatePendingRestart;
        }
        catch (Exception ex)
        {
            // e.g. "No VelopackLocator has been set" when Run() didn't execute.
            Log.Warning(ex, "Velopack UpdateManager init failed; app updates disabled");
            _manager = null;
            IsInstalled = false;
        }

        var current = _manager?.CurrentVersion?.ToString()
            ?? typeof(AppUpdateService).Assembly.GetName().Version?.ToString(3) ?? "?";
        _status = !IsInstalled
            ? new AppUpdateStatus(AppUpdateState.NotInstalled, current)
            : _readyAsset is { } ready
                ? new AppUpdateStatus(AppUpdateState.ReadyToRestart, current, ready.Version.ToString(), 100)
                : new AppUpdateStatus(AppUpdateState.Idle, current);
        Log.Information("App update: installed={Installed} portable={Portable} version={Version} pending={Pending} cwd={Cwd}",
            IsInstalled, _manager?.IsPortable ?? false, current, _readyAsset?.Version, Environment.CurrentDirectory);
    }

    private static UpdateManager CreateManager(IUpdateSource? source, IVelopackLocator? locator)
    {
        if (source is not null) return new UpdateManager(source, null, locator);
        var feed = Environment.GetEnvironmentVariable(FeedOverrideVariable);
        if (!string.IsNullOrWhiteSpace(feed))
        {
            Log.Warning("App update feed overridden by {Variable}: {Feed}", FeedOverrideVariable, feed);
            return new UpdateManager(feed, null, locator);
        }
        return new UpdateManager(new GithubSource(GithubRepoUrl, accessToken: null, prerelease: false), null, locator);
    }

    public bool IsInstalled { get; }
    public AppUpdateStatus Status => _status;
    public bool IsApplyScheduled => _scheduled is not null;
    public event Action<AppUpdateStatus>? StatusChanged;

    /// <summary>Test probe: true while a Velopack call (including an abandoned, timed-out check) is running.</summary>
    internal bool IsBusy => Volatile.Read(ref _busy) != 0;

    public bool IsAnotherInstanceRunning() => IsAnotherInstanceRunningCore();

    public async Task<AppUpdateStatus> CheckAsync(CancellationToken ct = default)
    {
        var mgr = _manager;
        if (mgr is null || !IsInstalled) return _status;
        if (_status.State == AppUpdateState.ReadyToRestart) return _status;   // already downloaded
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return _status;

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(_checkTimeout);
        Task<UpdateInfo?>? check = null;
        var releaseBusy = true;
        try
        {
            Publish(_status with { State = AppUpdateState.Checking, Error = null });
            check = Task.Run(() => mgr.CheckForUpdatesAsync());
            // CheckForUpdatesAsync takes no token: WaitAsync only stops *waiting*; the call keeps running.
            var info = await check.WaitAsync(waitCts.Token).ConfigureAwait(false);
            _pending = info;
            if (info is null)
            {
                Log.Information("App is up to date ({Version})", _status.CurrentVersion);
                return Publish(_status with { State = AppUpdateState.UpToDate, AvailableVersion = null });
            }
            var available = info.TargetFullRelease.Version.ToString();
            Log.Information("App update available: {Current} -> {Available}", _status.CurrentVersion, available);
            return Publish(_status with { State = AppUpdateState.Available, AvailableVersion = available, Progress = 0 });
        }
        catch (OperationCanceledException) when (waitCts.IsCancellationRequested)
        {
            if (check is { IsCompleted: false })
            {
                // Keep _busy until Velopack really returns so a retry can't start a second, overlapping
                // call; the late result is discarded.
                releaseBusy = false;
                _ = check.ContinueWith(ReleaseAfterAbandonedCheck, CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
            }
            if (ct.IsCancellationRequested) return Publish(_status with { State = AppUpdateState.Idle });
            Log.Warning("App update check timed out after {Timeout}", _checkTimeout);
            return Publish(_status with { State = AppUpdateState.Failed, Error = "Timed out waiting for GitHub. Try again in a minute." });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "App update check failed");
            return Publish(_status with { State = AppUpdateState.Failed, Error = ex.Message });
        }
        finally
        {
            if (releaseBusy) Volatile.Write(ref _busy, 0);
        }
    }

    private void ReleaseAfterAbandonedCheck(Task<UpdateInfo?> t)
    {
        if (t.IsFaulted) Log.Warning(t.Exception, "Abandoned app update check failed late"); // observes the exception
        else Log.Information("Abandoned app update check finished late; result discarded");
        Volatile.Write(ref _busy, 0);
    }

    public async Task<AppUpdateStatus> DownloadAsync(CancellationToken ct = default)
    {
        var mgr = _manager;
        var info = _pending;
        if (mgr is null || info is null || _status.State != AppUpdateState.Available) return _status;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return _status;

        try
        {
            Publish(_status with { State = AppUpdateState.Downloading, Progress = 0, Error = null });
            var last = -1;
            // DownloadUpdatesAsync honours ct, and awaiting Task.Run awaits the real task → finally is safe here.
            await Task.Run(() => mgr.DownloadUpdatesAsync(info, p =>
            {
                if (p == last) return;   // worker thread, 0..100
                last = p;
                Publish(_status with { Progress = p });
            }, ct), ct).ConfigureAwait(false);
            _readyAsset = info.TargetFullRelease;
            Log.Information("App update {Version} downloaded", _status.AvailableVersion);
            return Publish(_status with { State = AppUpdateState.ReadyToRestart, Progress = 100 });
        }
        catch (OperationCanceledException)
        {
            Log.Information("App update download canceled");
            return Publish(_status with { State = AppUpdateState.Available, Progress = 0 });
        }
        catch (Exception ex)
        {
            // Includes AcquireLockFailedException (another Velopack operation, e.g. a second instance, holds the lock).
            Log.Warning(ex, "App update download failed");
            return Publish(_status with { State = AppUpdateState.Failed, Error = ex.Message });
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    public bool ScheduleApplyOnExit()
    {
        var mgr = _manager;
        var asset = _readyAsset;
        if (mgr is null || asset is null || _status.State != AppUpdateState.ReadyToRestart) return false;
        _scheduled = new ScheduledApply(mgr, asset);
        Log.Information("App update {Version} will be applied after exit", asset.Version);
        return true;
    }

    public void CancelScheduledApply()
    {
        if (_scheduled is null) return;
        _scheduled = null;
        Log.Information("Scheduled app update canceled (app kept running)");
    }

    /// <summary>
    /// Called by Program.Main after App.Run() returns (settings saved, Serilog closed → Debug only).
    /// Update.exe waits up to 60 s for this process, then force-stops anything left under the install
    /// root, applies and restarts. If launching it fails, the package is applied on the next launch.
    /// </summary>
    internal static void ApplyScheduledUpdateOnExit()
    {
        var scheduled = _scheduled;
        if (scheduled is null) return;
        try
        {
            scheduled.Manager.WaitExitThenApplyUpdates(scheduled.Asset, silent: false, restart: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WaitExitThenApplyUpdates failed: {ex}");
        }
    }

    /// <summary>
    /// Another process running this exact exe? Velopack's apply step force-kills every process under the
    /// install root without a Closing prompt. Processes we can't inspect count as "yes" (conservative).
    /// </summary>
    internal static bool IsAnotherInstanceRunningCore()
    {
        var found = false;
        try
        {
            using var self = Process.GetCurrentProcess();
            var selfPath = Environment.ProcessPath;
            foreach (var p in Process.GetProcessesByName(self.ProcessName))
            {
                using (p)
                {
                    if (found || p.Id == self.Id) continue;
                    try { found = string.Equals(p.MainModule?.FileName, selfPath, StringComparison.OrdinalIgnoreCase); }
                    catch { found = true; }
                }
            }
        }
        catch
        {
            // Enumeration failed: assume we're alone.
        }
        return found;
    }

    private AppUpdateStatus Publish(AppUpdateStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(status);
        return status;
    }
}
```

### 6.5 `src/YtDlpGui/Services/BinaryResolver.cs` (Phase A: 후보 추가 + 테스트 seam. 부모 탐색은 그대로)
```csharp
using System.Diagnostics;
using System.IO;

namespace YtDlpGui.Services;

public sealed class BinaryResolver : IBinaryResolver
{
    /// <summary>Sub-folder of the data dir (next to settings.json). Survives app updates, unlike the install folder.</summary>
    public const string ManagedBinFolderName = "bin";

    private readonly ISettingsStore _settings;

    public BinaryResolver(ISettingsStore settings) { _settings = settings; }

    /// <summary>%LocalAppData%\YtDlpGui\bin — derived from the settings file so the data root is defined once.</summary>
    public static string GetManagedBinDirectory(ISettingsStore settings)
    {
        var dataDir = Path.GetDirectoryName(settings.SettingsFilePath);
        return string.IsNullOrEmpty(dataDir) ? string.Empty : Path.Combine(dataDir, ManagedBinFolderName);
    }

    public string? ResolveYtDlp() => Resolve("yt-dlp.exe", _settings.Current.YtDlpPathOverride,
        GetManagedBinDirectory(_settings), AppContext.BaseDirectory);
    public string? ResolveFfmpeg() => Resolve("ffmpeg.exe", _settings.Current.FfmpegPathOverride,
        GetManagedBinDirectory(_settings), AppContext.BaseDirectory);

    /// <summary>Order (pinned by BinaryResolverTests): override → tools folder → next to exe → parent dirs → PATH.</summary>
    internal static string? Resolve(string exeName, string? overridePath, string? managedBinDir, string baseDirectory)
    {
        // 0. Settings override wins if it points to a real file.
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;

        // 1. Update-safe tools folder. The installed app folder (…\current) is replaced on every update.
        if (!string.IsNullOrEmpty(managedBinDir))
        {
            var managed = Path.Combine(managedBinDir, exeName);
            if (File.Exists(managed)) return managed;
        }

        // 2. Same folder as the GUI exe (portable / dev builds).
        var sideBySide = Path.Combine(baseDirectory, exeName);
        if (File.Exists(sideBySide)) return sideBySide;

        // 3. Walk up to repo root (handy in dev: src/YtDlpGui/bin/... -> repo root has yt-dlp.exe).
        //    Unchanged in Phase A; becomes DEBUG-only in Phase B (peer ack).
        var dir = new DirectoryInfo(baseDirectory);
        ... (기존 루프 그대로)

        // 4. PATH lookup via `where` — 기존 코드 그대로
        ...
    }
}
```
`Path.GetDirectoryName("")`이 null을 반환하는 경우를 막는 가드는 반드시 필요하다. 없으면 `Path.Combine("", "bin")`이 CWD 기준 상대 경로가 되는데, D11 이후 CWD는 `%UserProfile%`이다.

### 6.6 `src/YtDlpGui/ViewModels/AppUpdateRestart.cs` (신규. Settings(A)와 배너(B)가 같은 재시작 절차를 공유한다)
```csharp
using System.Windows;
using YtDlpGui.Services;

namespace YtDlpGui.ViewModels;

/// <summary>"Restart to update" plumbing shared by Settings (Phase A) and the main-window banner (Phase B).</summary>
internal static class AppUpdateRestart
{
    /// <summary>Warns about other instances (Velopack force-kills them), then arms the post-exit hand-off.</summary>
    public static bool ConfirmAndSchedule(IAppUpdateService updates, Window? owner)
    {
        if (updates.IsAnotherInstanceRunning())
        {
            var answer = MessageBox.Show(owner!,
                "Another YtDlpGui window is open. Installing the update closes it immediately — its downloads " +
                "are cancelled without asking. Continue?",
                "Restart to update", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return false;
        }
        return updates.ScheduleApplyOnExit();
    }

    /// <summary>
    /// Exits through the normal main-window close so MainWindow.OnClosing's prompts (yt-dlp -U, active downloads)
    /// and App.OnExit's settings save still run. Close() is synchronous: still visible = the user said "No".
    /// </summary>
    public static void CloseMainWindow(IAppUpdateService updates)
    {
        var main = Application.Current?.MainWindow;
        if (main is null) { Application.Current?.Shutdown(); return; }
        main.Close();
        if (main.IsVisible) updates.CancelScheduledApply();
    }
}
```
(`owner`가 null이어도 되는 오버로드는 기존 `MessageBox.Show(ActiveOwner(), …)` 호출과 같은 방식으로 맞춘다.)

### 6.7 `src/YtDlpGui/ViewModels/SettingsViewModel.cs` (추가 위주. SelfUpdateYtDlpAsync, CheckVersionsAsync, GetExeVersionAsync는 건드리지 않는다)
```csharp
using System.Windows.Threading;               // DispatcherPriority
...
private readonly IAppUpdateService _appUpdates;

public SettingsViewModel(
    ISettingsStore store, IConfImporter importer, OptionsViewModel options,
    IBinaryResolver binaries, IYtDlpRunner runner,
    IAppUpdateService? appUpdates = null)    // Phase A: not in DI yet (App.xaml.cs peer-locked) → shared instance
{
    ... (기존 그대로)
    BinFolderPath = BinaryResolver.GetManagedBinDirectory(store);
    _appUpdates = appUpdates ?? AppUpdateService.Shared;
    _appUpdates.StatusChanged += OnAppUpdateStatusChanged;
    ApplyAppUpdateStatus(_appUpdates.Status);
}

public string BinFolderPath { get; }

// --- App self-update (Velopack / GitHub Releases) ---
[ObservableProperty] private string _appUpdateStatusText = string.Empty;
[ObservableProperty] private int _appUpdateProgress;
[ObservableProperty] private bool _isAppUpdateDownloading;
[ObservableProperty] private bool _isAppUpdateAvailable;
[ObservableProperty] private bool _isAppUpdateReady;

/// <summary>Called from SettingsWindow.Closed — the service outlives this transient VM.</summary>
public void OnWindowClosed() => _appUpdates.StatusChanged -= OnAppUpdateStatusChanged;

// Raised on a background thread; always render the latest snapshot.
private void OnAppUpdateStatusChanged(AppUpdateStatus _)
    => Application.Current?.Dispatcher.BeginInvoke(() => ApplyAppUpdateStatus(_appUpdates.Status));

private void ApplyAppUpdateStatus(AppUpdateStatus s)
{
    AppUpdateStatusText = DescribeAppUpdate(s);
    AppUpdateProgress = s.Progress;
    IsAppUpdateDownloading = s.State == AppUpdateState.Downloading;
    IsAppUpdateAvailable = s.State == AppUpdateState.Available;
    IsAppUpdateReady = s.State == AppUpdateState.ReadyToRestart;
    CheckAppUpdateCommand.NotifyCanExecuteChanged();
    DownloadAppUpdateCommand.NotifyCanExecuteChanged();
    RestartToUpdateCommand.NotifyCanExecuteChanged();
}

private static string DescribeAppUpdate(AppUpdateStatus s) => s.State switch { ... §4 문구 그대로 ... };

private bool CanCheckAppUpdate() => _appUpdates.IsInstalled
    && _appUpdates.Status.State is not (AppUpdateState.Checking or AppUpdateState.Downloading or AppUpdateState.ReadyToRestart);

[RelayCommand(CanExecute = nameof(CanCheckAppUpdate))]
private Task CheckAppUpdateAsync() => _appUpdates.CheckAsync();

private bool CanDownloadAppUpdate() => _appUpdates.Status.State == AppUpdateState.Available;

[RelayCommand(CanExecute = nameof(CanDownloadAppUpdate))]
private Task DownloadAppUpdateAsync() => _appUpdates.DownloadAsync();

private bool CanRestartToUpdate() => _appUpdates.Status.State == AppUpdateState.ReadyToRestart;

[RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
private void RestartToUpdate()
{
    if (!AppUpdateRestart.ConfirmAndSchedule(_appUpdates, ActiveOwner())) return;

    Cancel(); // close Settings like the Cancel button (reverts theme preview; unsaved edits are dropped)

    // Posted so it runs after the modal Settings loop has unwound.
    Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background,
        new Action(() => AppUpdateRestart.CloseMainWindow(_appUpdates)));
}

[RelayCommand]
private void OpenBinFolder()
{
    try { Directory.CreateDirectory(BinFolderPath); } catch { /* surfaced if open fails */ }
    OpenInExplorer(BinFolderPath);
}
```
`SeedConfDirectory()`는 override 분기를 유지하고, 부모 6단계 루프만 다음으로 바꾼다.
```csharp
// Same lookup the downloader uses (override → tools folder → next to app → PATH).
var ytDlp = _binaries.ResolveYtDlp();
var resolvedDir = string.IsNullOrEmpty(ytDlp) ? null : Path.GetDirectoryName(ytDlp);
return Directory.Exists(resolvedDir) ? resolvedDir : null;
```

### 6.8 `src/YtDlpGui/Views/SettingsWindow.xaml`
- `Window.Resources`에 `<BooleanToVisibilityConverter x:Key="BoolToVis" />`를 추가한다. 창마다 스코프가 따로라 MainWindow와 충돌하지 않는다.
- yt-dlp override 힌트를 "Empty = auto-detect: tools folder (see About), next to the app, then PATH."로 바꾼다.
- About의 `SelfUpdateStatus` TextBlock 뒤에 아래 블록을 넣는다.
```xml
<TextBlock Text="App updates" Style="{StaticResource SectionLabel}" />
<TextBlock Text="{Binding AppUpdateStatusText, Mode=OneWay}" TextWrapping="Wrap" />
<ProgressBar Height="4" Margin="0,6,0,0" Minimum="0" Maximum="100"
             Value="{Binding AppUpdateProgress, Mode=OneWay}"
             Visibility="{Binding IsAppUpdateDownloading, Converter={StaticResource BoolToVis}}" />
<StackPanel Orientation="Horizontal" Margin="0,8,0,0">
  <Button Padding="10,4" Content="Check for app updates" Command="{Binding CheckAppUpdateCommand}" />
  <Button Padding="10,4" Margin="6,0,0,0" Content="Download update" Command="{Binding DownloadAppUpdateCommand}"
          Visibility="{Binding IsAppUpdateAvailable, Converter={StaticResource BoolToVis}}" />
  <Button Padding="10,4" Margin="6,0,0,0" Content="Restart to update" Command="{Binding RestartToUpdateCommand}"
          ToolTip="Closes the app (unsaved changes in this dialog are discarded), installs the update and restarts."
          Visibility="{Binding IsAppUpdateReady, Converter={StaticResource BoolToVis}}" />
</StackPanel>
```
- 경로 목록에 `Tools folder:` + `{Binding BinFolderPath, Mode=OneWay}`를 추가한다. get-only 속성이라 Run에는 반드시 OneWay를 준다. 힌트는 "Put yt-dlp.exe / ffmpeg.exe here — this folder survives app updates.", 버튼은 "Open tools folder"(`OpenBinFolderCommand`)다.

### 6.9 `src/YtDlpGui/Views/SettingsWindow.xaml.cs` (피어 ACK 후에만)
생성자에 `Closed += (_, _) => vm.OnWindowClosed();` 한 줄을 추가한다. ACK를 받지 못하면 Phase B로 넘긴다.

### 6.10 `build/dotnet-tools.json` (신규)
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "vpk": { "version": "1.2.158", "commands": [ "vpk" ], "rollForward": false }
  }
}
```

### 6.11 `build/pack.ps1` (신규. **PowerShell 5.1 호환, ASCII 전용**)
PS 5.1은 BOM 없는 UTF-8을 CP949로 읽으므로 주석과 메시지는 영어로만 쓴다. `??`, `?.`, 삼항 연산자, `&&`, 인자 3개 이상의 `Join-Path`는 쓰지 않고 `[IO.Path]::Combine`을 쓴다. 네이티브 명령에 `2>&1`을 붙이지 않는다.
```powershell
<#
.SYNOPSIS  Build a Velopack release of YtDlpGui into <repo>\Releases.
.DESCRIPTION
  Version = <Version> in src/YtDlpGui/YtDlpGui.csproj (single source of truth).
  Publishes self-contained, NON-single-file win-x64, then `vpk pack`.
  -Delta first downloads the latest GitHub full package so vpk pack builds a delta automatically.
  -Upload is opt-in, creates a DRAFT unless -Publish, needs $env:GITHUB_TOKEN.
  Never run -Upload from automation: publishing is the repository owner's decision.
.EXAMPLE  powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -AllowDirty
.EXAMPLE  powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Delta -ReleaseNotes .\notes.md
.EXAMPLE  $env:GITHUB_TOKEN='...'; powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Delta -Upload
#>
[CmdletBinding()]
param([switch]$Delta, [string]$ReleaseNotes = '', [switch]$Upload, [switch]$Publish, [switch]$AllowDirty)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PackId = 'SLGYtDlpGui'   # PERMANENT - changing it breaks updates for installed users
$PackTitle = 'YtDlpGui'; $Authors = 'SLGarden'; $MainExe = 'YtDlpGui.exe'; $Rid = 'win-x64'
$RepoUrl = 'https://github.com/starlight97/SLGYtDlpGui'   # keep in sync with AppUpdateService.GithubRepoUrl

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Project    = [IO.Path]::Combine($RepoRoot, 'src', 'YtDlpGui', 'YtDlpGui.csproj')
$WorkDir    = [IO.Path]::Combine($RepoRoot, 'artifacts', 'velopack')   # git-ignored
$BuildDir   = [IO.Path]::Combine($WorkDir, 'build')                    # --artifacts-path: obj/bin away from src\
$PublishDir = [IO.Path]::Combine($WorkDir, 'publish')
$ReleaseDir = [IO.Path]::Combine($RepoRoot, 'Releases')                # git-ignored
$IconPath   = [IO.Path]::Combine($RepoRoot, 'src', 'YtDlpGui', 'Assets', 'app.ico')
$Manifest   = [IO.Path]::Combine($PSScriptRoot, 'dotnet-tools.json')

function Invoke-Checked([string]$Exe, [string[]]$Arguments) {
    Write-Host ('> {0} {1}' -f $Exe, ($Arguments -join ' ')) -ForegroundColor DarkGray
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('{0} exited with code {1}' -f $Exe, $LASTEXITCODE) }
}
function Invoke-Vpk([string[]]$Arguments) {
    Push-Location $PSScriptRoot    # local tool manifest (build\dotnet-tools.json) resolves from here
    try { Invoke-Checked 'dotnet' (@('tool', 'run', 'vpk') + $Arguments) } finally { Pop-Location }
}

# 1. Version (3-part SemVer2; vpk rejects 4-part)
$Version = (& dotnet msbuild $Project -nologo -getProperty:Version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Bad <Version> '$Version' in $Project" }

# 2. Velopack NuGet == vpk (vpk itself only warns on a mismatch)
$ref = Select-Xml -Path $Project -XPath "//PackageReference[@Include='Velopack']" | Select-Object -First 1
if ($null -eq $ref) { throw 'Velopack PackageReference not found.' }
$nugetVersion = $ref.Node.GetAttribute('Version')
$toolVersion = (Get-Content $Manifest -Raw | ConvertFrom-Json).tools.vpk.version
if ($nugetVersion -ne $toolVersion) { throw "Velopack $nugetVersion != vpk $toolVersion. Bump both together." }

# 3. Hygiene
$dirty = (& git -C $RepoRoot status --porcelain | Out-String).Trim()
if ($dirty -and -not $AllowDirty) { throw 'Uncommitted changes. Commit first, or -AllowDirty for a local test build.' }

# 4. Tools + publish (self-contained, folder, NOT single-file, no --framework)
Push-Location $PSScriptRoot; try { Invoke-Checked 'dotnet' @('tool', 'restore') } finally { Pop-Location }
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
Invoke-Checked 'dotnet' @('publish', $Project, '-c', 'Release', '-r', $Rid, '--self-contained', 'true',
    '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false', '--artifacts-path', $BuildDir, '-o', $PublishDir)
foreach ($f in @($MainExe, 'YtDlpGui.dll', 'Velopack.dll')) {
    if (-not (Test-Path ([IO.Path]::Combine($PublishDir, $f)))) { throw "Publish output missing $f" }
}

# 5. Optional: latest GitHub full package for a delta (0 releases -> vpk logs and returns normally)
if ($Delta) {
    $hasToken = [bool]$env:GITHUB_TOKEN
    if ($hasToken) { $env:VPK_TOKEN = $env:GITHUB_TOKEN }   # never on the command line (echo/process list)
    try { Invoke-Vpk @('download', 'github', '--repoUrl', $RepoUrl, '--outputDir', $ReleaseDir) }
    catch { Write-Warning "No previous release downloaded: $($_.Exception.Message). Full package only." }
    finally { if ($hasToken) { Remove-Item Env:VPK_TOKEN -ErrorAction SilentlyContinue } }
}

# 6. Refuse a version that isn't newer than full packages already in Releases\ (earlier local packs,
#    plus the latest GitHub full package when -Delta ran - vpk download writes only that .nupkg,
#    never releases.win.json). vpk pack has the same guard but may prompt; failing here keeps the
#    script non-interactive. Tags already on GitHub are also refused by `vpk upload github` (no --merge).
$core = [version](($Version -split '-')[0])
$pattern = '^' + [regex]::Escape($PackId) + '-(\d+\.\d+\.\d+)(-[0-9A-Za-z.-]+)?-full\.nupkg$'
foreach ($pkg in @(Get-ChildItem -Path $ReleaseDir -Filter '*-full.nupkg' -File -ErrorAction SilentlyContinue)) {
    if ($pkg.Name -eq ('{0}-{1}-full.nupkg' -f $PackId, $Version)) { throw "$($pkg.Name) already exists in $ReleaseDir. Bump <Version>." }
    if ($pkg.Name -match $pattern -and [version]$Matches[1] -gt $core) { throw "$($pkg.Name) is newer than $Version. Bump <Version> or clear test packages from $ReleaseDir." }
}

# 7. Pack (deltas are generated automatically when a previous full nupkg is in --outputDir)
$packArgs = @('pack', '--packId', $PackId, '--packVersion', $Version, '--packDir', $PublishDir, '--mainExe', $MainExe,
    '--packTitle', $PackTitle, '--packAuthors', $Authors, '--runtime', $Rid, '--outputDir', $ReleaseDir)
if (Test-Path $IconPath) { $packArgs += @('--icon', $IconPath) }
if ($ReleaseNotes) { $packArgs += @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path) }
Invoke-Vpk $packArgs

# 8. Optional, manual-only upload (uploads the assets listed in assets.win.json = this build only)
if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw 'Set $env:GITHUB_TOKEN (fine-grained PAT, Contents: read/write on this repo).' }
    if ($dirty) { throw 'Refusing to upload a build from a dirty tree.' }
    $head = (& git -C $RepoRoot rev-parse HEAD | Out-String).Trim()
    if (-not (& git -C $RepoRoot branch -r --contains $head | Out-String).Trim()) { throw "HEAD $head is not pushed; push first." }
    $up = @('upload', 'github', '--repoUrl', $RepoUrl, '--outputDir', $ReleaseDir, '--tag', "v$Version",
        '--releaseName', "YtDlpGui $Version", '--targetCommitish', $head)
    if ($Publish) { $up += '--publish' }
    $env:VPK_TOKEN = $env:GITHUB_TOKEN
    try { Invoke-Vpk $up } finally { Remove-Item Env:VPK_TOKEN -ErrorAction SilentlyContinue }
}
Write-Host "Done: $PackId $Version -> $ReleaseDir" -ForegroundColor Green
Get-ChildItem $ReleaseDir | Sort-Object LastWriteTime -Descending | Select-Object -First 8 Name, Length | Format-Table -AutoSize
```
예상 산출물(1.2.158 `DefaultName.cs`): `SLGYtDlpGui-win-Setup.exe`, `SLGYtDlpGui-win-Portable.zip`, `SLGYtDlpGui-0.4.0-full.nupkg`, (두 번째부터) `SLGYtDlpGui-0.4.1-delta.nupkg`, `releases.win.json`, `assets.win.json`, `RELEASES`.

### 6.12 테스트 (신규, `tests/YtDlpGui.Tests/`. `using Xunit; namespace YtDlpGui.Tests;` 스타일이고 피어 파일명과 겹치지 않는다. 네트워크는 쓰지 않는다)
- **`AppEntryPointTests.cs`**
  - `typeof(BinaryResolver).Assembly.EntryPoint?.DeclaringType?.FullName == "YtDlpGui.Program"`.
  - `Program.GetWorkingDirectoryOverride` 사례:
    - cwd와 appDir이 같으면 home을 반환한다(끝 구분자 유무 모두).
    - cwd가 appDir 하위면 home을 반환한다.
    - 다른 폴더면 null을 반환한다.
    - 이름만 비슷한 형제 폴더(`C:\app2` vs `C:\app`)면 null을 반환한다.
- **`BinaryResolverTests.cs`** (임시 루트, `IDisposable`로 정리)
  - 가짜 exe 이름은 고유하게 `ytdlpgui-resolver-test.exe`로 한다. PATH나 부모 폴더에 우연히 있는 파일과 겹칠 일이 없어 결정적이다.
  - `FakeSettingsStore(SettingsFilePath = <tmp>\data\settings.json)`를 쓴다.
  - 검증 항목:
    - ① `GetManagedBinDirectory`는 `<tmp>\data\bin`이다.
    - ② SettingsFilePath가 비어 있으면 빈 문자열이다.
    - ③ **tools 폴더가 exe 옆보다 우선한다**(새 우선순위를 피어용으로 고정).
    - ④ override가 tools 폴더보다 우선한다.
    - ⑤ override 파일이 없으면 tools 폴더로 떨어진다.
    - ⑥ tools 폴더가 없으면 exe 옆을 쓴다.
    - ⑦ 어디에도 없으면 null이다(where.exe 실패 경로).
- **`AppUpdateServiceTests.cs`**
  1. `Default_ctor_without_VelopackApp_Run_disables_updates`: `new AppUpdateService()`는 **init-failed 분기**를 탄다. 결과는 NotInstalled이고, `CheckAsync`는 예외 없이 NotInstalled를 반환하며, `ScheduleApplyOnExit()`는 false다.
  2. `Real_not_installed_locator`: `new AppUpdateService(new SimpleFileSource(new DirectoryInfo(tmp)), VelopackLocator.CreateDefaultForPlatform())`는 **실제 미설치 분기**를 탄다. 결과는 IsInstalled false와 NotInstalled이고, CheckAsync도 NotInstalled이며 NotInstalledException을 던지지 않는다.
  3. `Installed_empty_feed_is_up_to_date`: `TestVelopackLocator("SLGYtDlpGui","0.4.0",tmp)` + 빈 `SimpleFileSource`로 시작하면 Idle이다. CheckAsync는 UpToDate를 반환하고, StatusChanged는 Checking → UpToDate 순으로 발생한다.
  4. `Installed_feed_with_newer_release_is_available`: tmp의 `releases.win.json`에 아래 JSON을 넣는다. 결과는 Available이고 AvailableVersion은 "0.5.0"이다. `ScheduleApplyOnExit()`는 false다(아직 Ready가 아니므로).
     ```json
     {"Assets":[{"PackageId":"SLGYtDlpGui","Version":"0.5.0","Type":"Full","FileName":"SLGYtDlpGui-0.5.0-full.nupkg","SHA1":"0000000000000000000000000000000000000000","SHA256":"","Size":10}]}
     ```
  5. `Timed_out_check_keeps_busy_until_velopack_returns`: `BlockingUpdateSource : IUpdateSource`를 쓴다(`GetReleaseFeed`는 호출 수를 세고 TCS를 반환하고, `DownloadReleaseEntry`는 NotSupported). timeout은 100ms다.
     - 첫 CheckAsync는 Failed를 반환하고 `IsBusy`는 true다.
     - 재시도 CheckAsync는 **호출 수를 늘리지 않는다.**
     - TCS를 빈 feed로 완료하면 IsBusy가 false가 될 때까지 폴링한다(최대 5초).
     - 세 번째 CheckAsync는 UpToDate를 반환하고 호출 수는 1 늘어난다.
  6. `Caller_cancel_returns_idle`: 이미 취소된 토큰(또는 짧게 취소되는 토큰)과 BlockingUpdateSource를 쓴다. 결과는 Idle이다.
  - 인터페이스 시그니처(1.2.158 리플렉션): `Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string appId, string channel, Guid? stagingId = null, VelopackAsset latestLocalRelease = null)`, `Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)`. `VelopackAssetFeed.Assets`는 settable이다.
  - `TestVelopackLocator`는 version=null을 받지 못하므로(프로브) 미설치 테스트에는 반드시 `CreateDefaultForPlatform()`을 쓴다. 두 locator 모두 `VelopackLocator.Current`를 설정하지 않으므로 테스트 사이에 상태가 새지 않는다.
- **`AppUpdateTestDoubles.cs`**: `internal sealed class FakeAppUpdateService : IAppUpdateService`. NotInstalled 상태이고 모든 메서드는 no-op이다. 이벤트는 `add { } remove { }`로 CS0067을 피한다. Phase B에서 MainViewModel 테스트가 쓴다. Phase A에서 미리 만들어 두면 Phase B의 피어 테스트 수정이 인자 한 개 추가로 끝난다.

### 6.13 dev-docs
`02-context.md`에 D1~D13, 검증 사실, 피어 ACK 결과를 기록하고, `03-checklist.md`에 Phase A 세부 항목과 ACK 게이트를 추가한다.

---

## 7. Phase B — 피어 "완료" 이후
**게이트:** 피어 "완료", 피어 파일 커밋, SettingsViewModel 변경 안내 수신, Phase A 검증 통과, BinaryResolver DEBUG화와 피어 테스트 수정에 대한 ACK.

1. **`App.xaml.cs`**: `services.AddSingleton<YtDlpUpdater>();` 다음 줄에 `services.AddSingleton<IAppUpdateService, AppUpdateService>();`를 추가한다. DI는 public 생성자(파라미터 없음)를 쓴다.
2. **`AppUpdateService.cs`**: `SharedInstance`/`Shared`를 삭제한다.
3. **`SettingsViewModel.cs`**: `IAppUpdateService appUpdates`를 필수로 바꾸고 `_appUpdates = appUpdates;`로 한다. 피어 안내(예: SelfUpdateYtDlpAsync를 `YtDlpUpdater`에 위임)를 반영한다. 피어가 새 필수 파라미터를 추가하면 우리 파라미터보다 앞에 둔다. SettingsViewModel을 직접 생성하는 테스트는 없다.
4. **`MainViewModel.cs`** (생성자 마지막에 `IAppUpdateService appUpdates` 추가):
```csharp
private readonly IAppUpdateService _appUpdates;   // subscribe StatusChanged in ctor; ApplyAppUpdateBanner(_appUpdates.Status)
private bool _startupUpdateCheckStarted, _appUpdateBannerDismissed, _appUpdateBannerEngaged;

[ObservableProperty] private bool _hasAppUpdateBanner;
[ObservableProperty] private string _appUpdateBannerText = string.Empty;
[ObservableProperty] private string _appUpdateBannerActionText = "Update & restart";
[ObservableProperty] private int _appUpdateBannerProgress;
[ObservableProperty] private bool _isAppUpdateBannerDownloading;

public bool IsRestartingToUpdate => _appUpdates.IsApplyScheduled;   // read by MainWindow.OnClosing

/// <summary>Called from MainWindow.OnLoaded next to CheckYtDlpVersionAsync; never blocks first paint.</summary>
public async Task CheckAppUpdateInBackgroundAsync()
{
    if (_startupUpdateCheckStarted || !_appUpdates.IsInstalled) return;   // dev/F5: no network call
    _startupUpdateCheckStarted = true;
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false); // let first paint + yt-dlp probe go first
        await _appUpdates.CheckAsync().ConfigureAwait(false);           // banner driven by StatusChanged
    }
    catch (Exception ex) { Log.Warning(ex, "Background app update check failed"); }
}

private void OnAppUpdateStatusChanged(AppUpdateStatus _)
    => Application.Current?.Dispatcher.BeginInvoke(() => ApplyAppUpdateBanner(_appUpdates.Status));
// ApplyAppUpdateBanner: show = !dismissed && State in {Available, Downloading, ReadyToRestart}
//   (or Failed && _appUpdateBannerEngaged). Texts per §4; UpdateAppCommand.NotifyCanExecuteChanged().

private bool CanUpdateApp() => _appUpdates.Status.State is AppUpdateState.Available or AppUpdateState.ReadyToRestart;

[RelayCommand(CanExecute = nameof(CanUpdateApp))]
private async Task UpdateAppAsync()   // one click = download + restart (user decision)
{
    _appUpdateBannerEngaged = true;
    var s = _appUpdates.Status.State == AppUpdateState.ReadyToRestart
        ? _appUpdates.Status
        : await _appUpdates.DownloadAsync().ConfigureAwait(true);
    if (s.State != AppUpdateState.ReadyToRestart) return;
    if (AppUpdateRestart.ConfirmAndSchedule(_appUpdates, Application.Current?.MainWindow))
        AppUpdateRestart.CloseMainWindow(_appUpdates);
}

[RelayCommand]
private void DismissAppUpdate() { _appUpdateBannerDismissed = true; HasAppUpdateBanner = false; }
```
   yt-dlp "not found" 안내 문구 두 곳을 "(not found — put yt-dlp.exe in the tools folder (Settings > About) or on PATH)"로 바꾼다. 줄 번호는 피어 작업으로 계속 움직이므로(초안 42/149 → 비평 시점 52/180 → 현재 56/184) **Phase B 착수 시 `(not found` 로 다시 grep**한다.
5. **`MainWindow.xaml`**: 클립보드/피어 yt-dlp 배너와 같은 구조(Border + BoolToVis + Grid 3열)로 앱 업데이트 배너를 추가한다. 정보용 파란 계열(`#E3EEFF`/`#8FB3E8`/`#0B3A75`)로, TextBlock + 얇은 ProgressBar + `{Binding AppUpdateBannerActionText}` 버튼(`UpdateAppCommand`) + ✕(`DismissAppUpdateCommand`)로 구성하고 피어 배너와 겹치지 않게 배치한다. 선택 사항: Title "v0.4" 하드코딩을 버전 표시로 교체.
6. **`MainWindow.xaml.cs`**: 피어의 `OnLoaded` 패턴을 따라 `_ = vm.CheckYtDlpVersionAsync();` 다음 줄에 `_ = vm.CheckAppUpdateInBackgroundAsync();`를 추가한다. `OnClosing`의 active downloads 문구는 `vm.IsRestartingToUpdate`일 때 "… Cancel them and restart to install the update?"로 보여 준다(취소 동작은 같음).
7. **`tests/YtDlpGui.Tests/MainViewModelYtDlpBannerTests.cs`** (피어 파일, ACK 필요): `CreateVm()`의 `new MainViewModel(…, updater)`에 `new FakeAppUpdateService()`를 8번째 인자로 추가한다. 빠뜨리면 **테스트 프로젝트 컴파일이 깨진다.** `FakeAppUpdateService`는 Phase A의 `AppUpdateTestDoubles.cs`에 있다.
8. **`BinaryResolver.cs`**(ACK): 부모 6단계 루프를 `#if DEBUG` … `#endif`로 감싸고 주석을 "Dev only"로 바꾼다. `BinaryResolverTests`의 우선순위 테스트는 부모 탐색 전에 결정되므로 영향이 없다.
9. **README.md / README.ko.md**: 설치(Setup.exe, UAC 없음, SmartScreen 안내), 자동 업데이트, 도구 폴더 `%LocalAppData%\YtDlpGui\bin`, 새 탐색 순서, "설치판은 .NET 런타임 불필요(self-contained)", 다중 창 사용 시 업데이트 주의를 반영한다.
- YtDlpRunner에 WorkingDirectory를 명시하는 변경은 **하지 않는다.** D11이 모든 호출자를 한 번에 처리하므로 피어 코드를 건드릴 이유가 없다.

## 8. Phase C (선택) — net10.0-windows 리타깃
- 두 csproj의 `TargetFramework`를 `net10.0-windows`로 바꾼다. 사용하지 않는 `Microsoft.Extensions.Hosting`을 제거하고, `Microsoft.Extensions.DependencyInjection`은 10.0.x로 올린다(선택). 선택 사항으로 `<SatelliteResourceLanguages>en;ko</SatelliteResourceLanguages>`도 추가한다.
- 로컬 pubxml 두 개는 git 미추적이라 로컬에서만 바꾸거나 삭제한다. pack.ps1은 TFM을 하드코딩하지 않는다.
- WPF-UI 3.0.5와 CommunityToolkit.Mvvm 8.4.0은 net8 자산 fallback으로 동작한다. 스모크 테스트로 확인한다(테마 System/Light/Dark, 모든 창).
- self-contained라 사용자에게는 일반 업데이트다. 런타임이 바뀌어 delta가 커진다. 로컬 피드로 net8 → net10 E2E를 수행한다.
- **권장 시점:** 피어 작업이 끝난 뒤, 첫 공개 릴리스 전. .NET 8 지원 종료(2026-11-10)까지 약 6주 남았다.

---

## 9. 검증 현황
**확인 완료** (1.2.158 소스, 문서, 프로브, 검증자):
- **API**
  - `VelopackApp.Build().SetAutoApplyOnStartup(bool)`(기본 ON) `.Run()`. Run은 인자 없는 Main에서도 동작한다(`GetCommandLineArgs`).
  - `UpdateManager(IUpdateSource | string, UpdateOptions?, IVelopackLocator?)`. 파라미터 없는 생성자는 Flow 소스이므로 쓰지 않는다.
  - `GithubSource(repoUrl, accessToken, prerelease, downloader?)`.
  - `CheckForUpdatesAsync()`(CT 없음, 미설치 시 NotInstalledException), `DownloadUpdatesAsync(info, Action<int>?, CT)`(lock 사용, AcquireLockFailedException), `WaitExitThenApplyUpdates(asset, silent, restart, args)`, `ApplyUpdatesAndRestart` = Wait… + Exit(0).
  - `IsInstalled`, `IsPortable`, `CurrentVersion`, `UpdatePendingRestart`.
- **locator**
  - Run() 없이 locator 없이 `UpdateManager`를 만들면 `InvalidOperationException`이 난다.
  - `CreateDefaultForPlatform()`은 미설치 상태를 올바르게 보고한다.
  - `TestVelopackLocator(…,"0.4.0",…)` + `SimpleFileSource`의 null/0.5.0 결과를 확인했다. version=null은 예외다.
- **설치 동작**
  - 설치 레이아웃과 설치/제거 때 루트 전체 교체/삭제(D1 확정).
  - 바로가기와 재시작의 CWD는 `current`(D11).
  - 60초 후 강제 진행과 루트 아래 프로세스 강제 종료(D3, D12).
- **vpk**
  - vpk 진입점 검사는 StartupObject 구성에서 통과한다.
  - 사용한 플래그는 모두 존재하고, `VPK_TOKEN`을 쓴다.
  - 출력 파일명은 위 목록과 같다.
  - delta는 이전 full nupkg가 있으면 자동으로 생긴다.
  - `vpk download github`는 최신 full nupkg만 받고, 릴리스가 0개면 정상 반환한다.
  - pack은 버전 중복 또는 역행 시 경고하거나 거부한다. upload는 기존 태그를 거부하고, 최신 빌드 자산만 올린다.
- **GitHub와 피드**
  - GithubSource는 최신 10개 릴리스를 읽고 draft는 보지 않는다.
  - 비인증 한도는 목록 호출에만 적용된다. 릴리스가 0개면 CheckForUpdatesAsync가 null(UpToDate)을 반환한다.
  - `SimpleFileSource` 로컬 피드가 동작한다(D10).
- **빌드 환경**
  - `build/dotnet-tools.json` 매니페스트를 탐색하고, `dotnet tool run`은 인자를 그대로 전달한다.
  - MS DI는 선택적 파라미터를 처리한다.
  - SDK artifacts 모드를 쓸 수 있다.

**미검증(실행 시 확인):**
- vpk pack의 대화형 프롬프트가 PS 콘솔에서 어떻게 동작하는지. §6.11 step 6이 먼저 막으므로 영향이 작다.
- WPF Shutdown 중 Closing 동작. 설계가 여기에 의존하지 않는다.
- 이 PC에서 SmartScreen과 Smart App Control이 실제로 어떻게 반응하는지.
- Velopack 내부 HTTP 타임아웃 값. D13의 busy 유지 시간 상한을 정한다.

## 10. 검증 계획
**Phase A**
1. 빌드와 테스트는 피어와 격리한다: `dotnet build YtDlpGui.sln -c Release --artifacts-path artifacts\verify`, `dotnet test tests\YtDlpGui.Tests\YtDlpGui.Tests.csproj -c Release --artifacts-path artifacts\verify`. 오류가 0이고 신규 테스트를 포함해 모두 통과해야 한다. 피어 테스트 5개도 계속 통과해야 한다.
2. 미설치 실행(`artifacts\verify\bin\YtDlpGui\release\YtDlpGui.exe`를 탐색기에서 실행):
   - About에 NotInstalled 문구가 나오고 버튼이 비활성인지 확인한다.
   - 로그에 `installed=False … cwd=C:\Users\<user>`가 찍히는지 확인한다(D11).
   - 기존 다운로드, 설정 저장, 피어의 yt-dlp 배너와 -U가 정상 동작하는지 확인한다.
3. `build\pack.ps1 -AllowDirty`(0.4.0)를 실행한다. pack 로그에 "Verified VelopackApp.Run() in …Program::Main"가 있고, `Releases\`에 Setup/Portable/full nupkg/feed가 생기는지 확인한다.
4. Setup.exe로 설치한다.
   - UAC가 뜨지 않아야 하고, 설치 위치는 `%LocalAppData%\SLGYtDlpGui\current\YtDlpGui.exe`, 바로가기 이름은 "YtDlpGui"여야 한다.
   - 기존 settings(테마, 출력 폴더)가 유지되고 `%LocalAppData%\YtDlpGui`가 그대로여야 한다.
   - About에서 Check를 누르면 GitHub(릴리스 0개) 조회 결과가 **UpToDate**여야 한다.
5. 도구 폴더: "Open tools folder"로 폴더를 열고 yt-dlp.exe를 넣는다. 메인 화면의 yt-dlp 경로가 bin을 가리키는지 확인한다.
6. 로컬 업데이트 E2E: `<Version>`을 0.4.1로 임시 변경해 pack하고, `$env:YTDLPGUI_UPDATE_FEED='<repo>\Releases'`를 설정한 뒤 설치된 exe를 실행한다.
   - Check → Available → Download(진행률) → Ready → Restart 순으로 진행한다.
   - 진행 중인 다운로드가 있을 때 "No"를 누르면 앱이 유지되고 Ready도 유지돼야 한다. "Yes"를 누르면 재시작 후 0.4.1이 떠야 한다.
   - settings, logs, bin이 유지되고, 로그에 "will be applied after exit"와 LastOptions 저장 기록이 있어야 한다. `%LocalAppData%\velopack\velopack_SLGYtDlpGui.log`에서 적용 기록을 확인한다.
   - **포맷 조회(yt-dlp)가 실행 중일 때 Restart**해도 적용이 성공해야 한다(D11 회귀).
   - 다운로드만 하고 그냥 종료했다면 다음 실행 때 자동 적용돼야 한다.
7. **다중 인스턴스**(D12):
   - 창 두 개를 띄우고 #1에서 다운로드만 한 뒤 #2를 실행한다. #2는 auto-apply를 하지 않고(#1이 살아 있음) About에 Ready가 표시돼야 한다.
   - #2에서 Restart를 누르면 경고가 떠야 하고, No를 누르면 아무 일도 일어나지 않아야 한다.
8. 제거: `%LocalAppData%\SLGYtDlpGui`만 삭제되고 `%LocalAppData%\YtDlpGui`는 남아야 한다.
9. 정리: Version을 0.4.0으로 되돌리고 테스트용 `Releases\`를 삭제한다. step 6 가드 때문에 0.4.1 흔적이 남아 있으면 0.4.0 pack이 거부된다.

**Phase B**
- 설치판 + 로컬 피드로 약 3초 뒤 배너가 떠야 한다. 한 번 클릭으로 진행률 → 재시작 → 새 버전까지 이어져야 한다.
- 진행 중인 다운로드가 있을 때 바뀐 OnClosing 문구가 나오고, No를 누르면 "Restart now"로 바뀌어야 한다. ✕로 닫으면 세션 동안 다시 뜨지 않아야 한다.
- 오프라인이면 배너 없이 로그만 남고, F5면 배너와 네트워크 호출이 없어야 한다.
- 배너와 Settings가 같은 singleton 상태를 공유해야 한다.
- 피어 테스트(8번째 인자)를 포함해 전체 테스트가 통과해야 한다.
- Release 빌드에서 부모 탐색을 하지 않는지 확인하고, 피어의 -U와 --ffmpeg-location이 정상인지 확인한다.

**Phase C**: clean restore → 빌드와 테스트 → 테마 스모크 → pack → net8 → net10 로컬 업데이트 E2E.

**공통**: 각 단계는 code-reviewer를 거치고, 커밋할 때는 우리 파일만 경로를 지정해 `git add` 한다.

## 11. 리스크 요약
risks 필드에 전체 목록이 있다. 핵심은 다음 다섯 가지다.
1. 동시 작업 중 트리가 깨질 수 있다. Program.cs와 StartupObject는 연달아 적용하고, restore 타이밍은 피어와 조율한다.
2. packId는 영구적이다.
3. 다른 인스턴스는 강제 종료된다. auto-apply 억제와 경고로 완화한다.
4. 서명이 없어 SmartScreen 경고가 뜬다.
5. Phase B에서 피어 테스트를 수정해야 한다.

---

## 12. 검토 반영
| # | 출처/심각도 | 지적 | 반영 |
|---|---|---|---|
| V1 | Velopack 검증 / major | Phase B에서 MainViewModel 생성자 인자를 추가하면 `MainViewModelYtDlpBannerTests.cs:35`(인자 7개)가 컴파일되지 않는다 | **수용.** Phase A에서 `AppUpdateTestDoubles.cs`(FakeAppUpdateService)를 미리 만든다. Phase B 파일 목록에 피어 테스트를 넣고(ACK), `new FakeAppUpdateService()`를 8번째 인자로 추가한다. 선택적 파라미터 대안은 null-object 분기가 VM에 남으므로 기각했다(§7-7). |
| V2 | minor | 설치판 CWD = current. 자식 yt-dlp가 current를 잠그고, 상대 경로가 current 아래로 해석된다 | **수용(D11).** Program.Main에서 `Run()` 직후 CWD가 앱 폴더 안이면 `%UserProfile%`로 옮긴다. 판정 함수를 테스트하고, 로그에 cwd를 기록하며, E2E에 "포맷 조회 중 Restart"를 넣고, 리스크에도 추가했다. 선택 제안이던 YtDlpRunner 명시 WD는 피어 코드를 건드리지 않아도 모든 호출자를 커버하므로 채택하지 않았다. 피어 ACK 항목이다. |
| V3 | minor | D2 기각 사유("Page glob 중복 위험")가 틀렸다 | **수용.** 사유를 철회하고, 공식 가이드와 의도적으로 다른 선택이며 빌드와 vpk 검사로 검증됐다고 다시 썼다. Page 전환도 동등한 대안으로 명시했다. D2 결정 자체는 유지한다. |
| V4 | minor | 60초 후 "포기"가 아니라 강제 종료 후 적용이다 | **수용.** D3과 리스크 문구를 정정했다. 핸드오프가 OnExit 이후라 무해하다는 점도 적었다. |
| V5 | minor | 다른 인스턴스 강제 종료는 미검증이 아니라 확정된 리스크다 | **수용 + 강화(D12).** 확정 리스크로 옮겼다. 제안(재시작 경고)에 더해, 시작 시 auto-apply가 첫 창을 죽이는 경로를 `SetAutoApplyOnStartup(!anotherInstance)`로 막았다. 남은 패키지는 `UpdatePendingRestart`로 ReadyToRestart에 복원한다. 경고는 Phase A(Settings)부터 넣었고, mutex는 후속 과제다. |
| V6 | minor | pack step 6이 `releases.win.json`을 검사하지만 download는 이 파일을 쓰지 않는다 | **수용.** `*-full.nupkg` 파일명에서 버전을 파싱해 같은 버전이나 더 높은 버전이 있으면 거부하도록 바꿨다. 주석을 고치고, vpk pack과 upload에 자체 가드가 있다는 점도 명시했다. |
| V7 | minor | 단위 테스트가 not-installed가 아니라 init-failed 분기를 탄다 | **수용(부분 수정).** internal 생성자(source, locator, timeout)와 InternalsVisibleTo를 추가했다. 단, 제안된 "TestVelopackLocator에 CurrentlyInstalledVersion=null"은 **불가능**하다. 프로브 결과 version=null이면 ArgumentException이 난다. 그래서 미설치 분기는 `VelopackLocator.CreateDefaultForPlatform()`으로 테스트하고(프로브: IsInstalled=False, NotInstalledException), 설치 분기는 `TestVelopackLocator("0.4.0")` + `SimpleFileSource`로 UpToDate와 Available을 테스트한다. init-failed 분기 테스트도 1건 유지한다. §9의 "생성자가 예외를 던지지 않음" 주장은 "Run() 이후에만 성립"으로 정정했다. |
| C1 | 회귀 비평 / major | CheckAsync 타임아웃 후 `_busy`가 풀려 Velopack 호출이 겹친다 | **수용(D13).** 대기를 끊었는데 내부 Task가 살아 있으면 continuation에서 busy를 푼다. 늦은 결과는 폐기하고 예외는 관찰한다. 세대 카운터만으로는 호출 중첩을 막지 못해 busy 유지를 택했다. BlockingUpdateSource 테스트로 "재시도가 호출 수를 늘리지 않음"을 고정한다. |
| C2 | major | BinaryResolver 순서 변경, 특히 부모 탐색 DEBUG화는 피어 소비자의 의미를 바꾼다 | **수용 + 범위 축소.** Phase A는 tools 폴더 후보 **추가만** 한다. 이 폴더는 현재 없으므로 모든 소비자의 결과가 같다. 부모 탐색 DEBUG화는 **Phase B로 옮기고 별도 ACK**를 받는다. 두 항목 모두 공지가 아닌 **ACK 필수**다. `Resolve`를 internal seam으로 바꾸고 `BinaryResolverTests`로 "tools 폴더 > exe 옆"을 명시적으로 고정했다. |
| C3 | minor | SettingsWindow.xaml.cs는 허용 목록 밖인데 공지만으로 수정하려 했다 | **수용.** Phase A 게이트를 **명시적 ACK**로 올렸다. 거부되면 Phase B로 미루고, 임시 VM 누수는 무해하다는 점을 수용하고 문서화했다(D5, §6.0). |
| C4 | minor | MainViewModel 줄 번호 42/149가 틀렸다 | **수용.** 현재는 56/184로 또 움직였다. 설계서에서 줄 번호 인용을 없애고 "Phase B 착수 시 `(not found` 재grep"으로 바꿨다. |
| C5 | 통과 기록 | Phase A 파일과 피어 잠금 파일 사이에 텍스트 중복이 없고, 선택적 DI와 StartupObject 방식이 타당하다 | 변경 없음. 결론을 그대로 유지한다. |

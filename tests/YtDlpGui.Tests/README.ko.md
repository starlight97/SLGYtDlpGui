# YtDlpGui.Tests

> English: [README.md](README.md)

본 앱(`YtDlpGui`)에서 **UI에 의존하지 않고 실제 yt-dlp를 호출하지도 않는**
순수 로직 부분만 자동 검증하는 단위 테스트 프로젝트입니다. 실행 시간은
밀리초 단위.

## 왜 만들었나

메인 프로젝트는 WPF 앱이지만, 비-UI 클래스에 들어 있는 로직 중
**GUI와 yt-dlp CLI 사이의 계약을 정의하는 4개 클래스**가 있습니다:

- 잘못 구현되면 다운로드가 미묘하게 깨짐.
- `NA` 값 / 따옴표 / 빈 필드 / 3-state 불리언 / 유니코드 정규화 등
  실수하기 쉬운 엣지 케이스를 다수 갖고 있음.
- GUI 클릭으로 검증하는 것보다 `dotnet test`로 돌리는 게 자릿수 단위로
  빠름.

그래서 이 4개만 테스트로 못 박아둡니다. XAML 바인딩, 다이얼로그 흐름,
실제 yt-dlp 서브프로세스 호출 같은 건 의도적으로 범위 밖.

## 무엇을 검증하나

| 대상 | 파일 | 테스트 수 | 무엇을 보장 |
|---|---|---|---|
| `Infrastructure.ArgBuilder` | [ArgBuilderTests.cs](ArgBuilderTests.cs) | 19 | `DownloadOptions` → yt-dlp argv 변환. SPEC §4 always-on 프리픽스, 빈 필드 미방출, `--cookies-from-browser browser:profile` 합성, 3-state playlist, `ExtraArgs` whitespace 분할, 자막 풀세트, `FormatForLog` 따옴표 처리. |
| `Infrastructure.ProgressParser` | [ProgressParserTests.cs](ProgressParserTests.cs) | 19 | `PROGRESS:` 라인 파싱, `NA` → `null` 변환, 깨진/짧은 라인 거부, 12개 `[stage]` 태그(`[download]`, `[Merger]`, `[ffmpeg]` 등) → 상태 추론, 대소문자 구분. |
| `Services.ConfImporter` | [ConfImporterTests.cs](ConfImporterTests.cs) | 23 | yt-dlp.conf 파싱: `--flag=value` 문법, 주석, 작은/큰따옴표, `--no-` 부정, 별칭(`--add-metadata` → embed-metadata), 3-state playlist, `--cookies-from-browser browser:profile`, always-on 플래그 silently 무시. shlex 백슬래시 한계도 명시적으로 테스트로 박아둠. |
| `Infrastructure.Hangul.NfcNormalizer` | [NfcNormalizerTests.cs](NfcNormalizerTests.cs) | 5 | 한글 NFD → NFC 파일명 리네임, 이미 NFC면 no-op, 누락 파일 no-op, 폴더 일괄 처리 + `modifiedSince` 필터. |

**총 66 테스트**, 전부 통과.

## 검증하지 않는 것

- **WPF UI** — 뷰모델, 바인딩, 다이얼로그. UI 자동화는 별도 도구(FlaUI,
  Appium 등) 필요. v1.0+ 작업.
- **`YtDlpRunner` 서브프로세스 호출** — 실제 `yt-dlp.exe`를 띄움. 통합
  테스트는 샘플 비디오와 네트워크 의존성이 필요해서 보류.
- **설정 / 프로필 저장소 파일 I/O** — 앱 부팅 시 항상 거치는 경로라
  별도 테스트의 비용 대비 효용 낮음.

## 실행

```bash
# 전체
dotnet test YtDlpGui.sln

# 클래스 단위
dotnet test YtDlpGui.sln --filter "FullyQualifiedName~ArgBuilderTests"

# 메서드 단위
dotnet test YtDlpGui.sln --filter "Name=Build_DownloadPlaylistTrue_EmitsYes"

# 커버리지 (coverlet 필요 — 선택)
dotnet test YtDlpGui.sln --collect:"XPlat Code Coverage"
```

VS / Rider에서는 Test Explorer가 자동 인식. 메서드 옆 ▶ 버튼으로 한 개씩
실행 가능.

## 새 테스트 추가하기

1. 대상이 비-UI 클래스인지 확인. `System.Windows.*`을 만지거나
   `Dispatcher`가 필요하면 여기 들어오면 안 됨.
2. 해당 클래스 전용 `*Tests.cs`를 찾거나, 새 파일 추가.
3. 단발성은 `[Fact]`, 같은 로직을 여러 입력에 돌리려면
   `[Theory] + [InlineData(...)]` (예시:
   `ProgressParserTests.TryInferStatus_KnownTags`).
4. 이름 규칙: `Method_Scenario_ExpectedOutcome` —
   예: `Build_CookiesFromBrowserWithProfile_EmitsBrowserColonProfile`.

스켈레톤:

```csharp
[Fact]
public void Build_NewFlag_EmittedWhenSet()
{
    var opts = new DownloadOptions { /* ... */ };
    var args = ArgBuilder.Build(opts, "https://x");
    Assert.Contains("--new-flag", args);
}
```

## 프로젝트 설정 메모

- `TargetFramework=net8.0-windows` + `UseWPF=true` — 메인 프로젝트와 TFM
  정렬해서 `ProjectReference`가 깔끔하게 풀리도록. 실제로 WPF 타입을
  끌어오는 테스트는 없음.
- `IsPackable=false` — 실수로 NuGet 패키지에 묶이지 않도록.
- `NfcNormalizerTests`는 실행마다 `%TEMP%\YtDlpGuiTests-<guid>` 디렉토리에
  임시 파일을 만들고 `Dispose`에서 삭제. 테스트가 중간에 죽으면 잔여
  폴더가 남을 수 있는데 직접 지워도 안전.
- `ProgressParserTests`의 Theory는 현재 `ProgressParser.TryInferStatus`가
  인식하는 12개 stage 태그를 모두 커버. 새 태그 매핑을 추가하면 같은
  커밋에서 `[InlineData]` 행도 함께 추가할 것.

## SPEC과의 매핑

테스트는 SPEC에 명시된 불변식을 의도적으로 인코딩해서, 회귀가 발생하면
다운로드가 망가지기 전에 테스트가 빨갛게 뜨도록 합니다:

- **SPEC §4 / §7**: always-on 프리픽스(`--ignore-config`, `--no-color`,
  `--newline`, `--no-progress`, `--progress-template`)가 모든
  `ArgBuilder.Build` 호출에 들어가는지 검증.
- **SPEC §6**: `PROGRESS:` 템플릿 파싱 — `NA`는 0이나 예외가 아니라
  `null`로.
- **SPEC §7**: 빈 / 기본 옵션은 argv에 안 들어감 (커맨드라인을 짧고
  점검 가능하게 유지).
- **SPEC §13**: stderr `ERROR:` 의미론은 여기 범위 밖(`YtDlpRunner`
  소관). 단, stage 태그 → 상태 추론은 검증.
- **SPEC §4.1**: conf import — 알려진 플래그는 옵션에 매핑, 모르는 건
  별도 `UnrecognizedFlags` 리스트로, always-on은 silently 드롭.
- **SPEC §8**: NFC 파일명 정규화 round-trip.

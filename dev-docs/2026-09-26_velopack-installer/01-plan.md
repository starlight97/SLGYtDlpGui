# Velopack 설치파일 + 자동업데이트 플랜

## 목표
- YtDlpGui를 Velopack 설치 파일(`Setup.exe`)로 배포한다.
- 사용자 폴더(`%LocalAppData%\YtDlpGui`)에 설치 → 설치/실행 시 UAC 프롬프트 없음.
- 자동 업데이트: GitHub Releases(`starlight97/SLGYtDlpGui`) 기반.
  - 앱 시작 시 백그라운드 자동 확인 → 새 버전 있으면 알림 → 사용자가 누르면 업데이트
  - 메뉴 "업데이트 확인"으로 수동 확인

## 접근 방식
- **publish**: self-contained, 단일 파일 아님(폴더), `win-x64` — 호환성/시작 속도/백신 오탐 측면에서 가장 안정적.
- **패키징**: Velopack(MIT, 무료) `vpk pack`. 유료 호스팅(Velopack Flow)은 사용하지 않음.
- **업데이트 소스**: `UpdateManager` + `GithubSource`.
- 제외한 대안
  - Inno Setup: 무료지만 자동 업데이트 없음.
  - 단일 exe(A/B): 설치 파일과 조합 시 이점 없음, `Assembly.Location` 등 경로 호환성 문제.
  - 코드 서명: 이번 범위 제외 (설치 시 SmartScreen 1회 경고 감수).

## 구현 순서
1. 조사 — 빌드 설정 / 진입점·UI / 파일 경로 + 교차 검증 (analyzer 워크플로우)
2. 설계 — architect
3. csproj — Velopack 패키지, 버전(SemVer), publish 설정, (필요 시) 타깃 프레임워크
4. 커스텀 Main(`Program.cs`) + `VelopackApp.Build().Run()`
5. yt-dlp/ffmpeg 경로 — 업데이트에도 안 지워지는 `%LocalAppData%\YtDlpGui\bin\` 탐색 후보 추가 (설정/프로필/로그는 이미 `%LocalAppData%\YtDlpGui\`에 있어 이전 불필요 — 조사로 확인)
6. `UpdateService` + 시작 시 자동 확인 + 메뉴 "업데이트 확인" UI
7. 패키징 스크립트 — `dotnet publish` → `vpk pack`
8. 빌드/패키징 검증(builder), 테스트(tester)
9. 코드리뷰(code-reviewer) → 커밋

## 리스크 / 주의사항
- Velopack은 업데이트마다 `current` 폴더를 통째로 교체 → exe 옆에 쓰는 파일(설정, yt-dlp.exe, ffmpeg, 로그)은 유실된다.
- WPF 자동 생성 Main과 커스텀 Main 충돌 (`StartupObject` 지정 또는 App.xaml 빌드 액션 변경).
- 피어 세션 **"YouTube 다운로드 실패"** 가 같은 리포에서 작업 중 → 겹치는 파일은 메시지로 조율 후 수정.
- .NET 8/9는 2026-11 지원 종료 → 타깃 프레임워크 확인 후 net10.0 검토.
- 코드 서명 없음 → SmartScreen(설치 시 1회), Windows 11 Smart App Control 켜진 PC에서는 차단 가능.

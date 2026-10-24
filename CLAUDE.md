# 쏙(Ssok) 작업 안내 — Claude용

사진·PDF·한글(hwp·hwpx) 파일을 **목표 용량에 맞춰** 줄이는 윈도우 프로그램. 학교 선생님들이 제출 용량 제한을 맞출 때 쓴다.
만든 사람(저장소 주인)은 개발자가 아닌 초등 교사다. 설명은 쉬운 한국어로, 결정이 필요하면 짧게 묻는다.

## 가장 중요한 원칙 (사용자가 직접 정한 것)

1. **목적 1순위는 목표 용량에 맞추는 것.** 어떤 방식이든 목표 이하가 되게 한다. "목표 미달"은 정말 더 줄일 수 없을 때만.
2. **화면은 단순하게.** 어려운 설명·선택지(TMI)는 넣지 않는다. PDF는 "글자 검색·복사가 안 될 수 있다" 정도만 안내.
3. **디자인 유지.** 초록 로고, 왼쪽 설정 · 오른쪽 끌어다 놓기/목록 화면. 기능: 파일마다/모두 합쳐서 목표, 사진 저장 형식, 위치정보 지우기, PDF 흑백, 웹하드식 체크·다운로드, 줄마다 비교·⬇·✕(목록에서 빼기).
4. **앱 안내문은 다나까체, 딱딱하지 않게.** (예: "줄여요", "저장합니다")
5. **결과 이름은 언제나 `원래이름_쏙.확장자`.** `_쏙`이 붙은 파일도 그대로 넣고 다시 줄일 수 있어야 한다 (이름으로 거르지 않음).
6. 원본은 절대 수정하지 않는다. 파일·이름·내용은 인터넷으로 보내지 않는다. 로그에 경로·파일명 금지.
7. 오픈소스 부품은 MIT·Apache-2.0·BSD·MPL-2.0만. GPL·AGPL 금지.

## 구조

| 위치 | 내용 |
| --- | --- |
| `src/Ssok.Core` | 목표 크기 탐색(SizeSearch), 안전 저장(SafeOutput, `_쏙`), 로그. 화면·네트워크 없음 |
| `src/Ssok.Core/Documents` | hwp(OpenMcdf)·hwpx(zip)·PDF(qpdf) 속 그림 바꿔 끼우기, 단계표(DocumentShrinker 16단계, RasterSearch 15단계) |
| `src/Ssok.App` | WPF 화면(MainWindow), WIC 그림 압축(WicRecoder), PDF 쪽→그림(PdfRasterizer·PdfPageShrink), 업데이트(Updater), 자가 시험(SelfTest) |
| `tests/Ssok.Core.Tests` | 외부 패키지 없는 시험 프로그램 (`dotnet run`) |
| `installer/Ssok.iss` | Inno Setup 설치판 (관리자 권한 없이 `%LocalAppData%\Programs\Ssok`) |
| `.github/workflows/release.yml` | 공식 배포 자동화 |
| `docs/` | 다운로드 홈페이지 (GitHub Pages, main 브랜치 /docs). 다운로드 단추는 최신 Release 파일을 자동으로 찾아감 |
| `tools/make-update-key.*` | 업데이트 서명 키 만들기 (이미 만들어 GitHub 비밀값 `SSOK_UPDATE_KEY`에 넣음 — 다시 만들지 말 것) |

알아 둘 동작:
- 한글 문서 속 BMP·GIF·투명 없는 PNG는 JPG로 바꾸고, hwpx는 `content.hpf`의 href·media-type, hwp는 DocInfo의 확장자(세 글자)와 BinData 스트림 이름을 함께 고친다.
- PDF는 그림만 줄여 보고, 못 맞추면 쪽을 그림으로 바꾼다(검색·복사 불가). 둘 다 못 맞추면 더 작은 쪽.
- "모두 합쳐서"는 1차로 비례 배분 → 합계가 넘치면 여유 있는 파일을 다시 줄여 맞춘다.
- 자가 시험 모드와 앱 종료는 `TerminateProcess`로 끝낸다 (WinRT PDF 렌더러 정리 중 0xC0000005 방지).
- 업데이트: 하루 한 번 `releases/latest/download/update.json`+`.sig` 확인, ECDSA P-256 서명과 SHA-256 지문이 맞을 때만 설치판을 조용히 실행. 저장소 주소·공개 키는 CI 빌드에서만 들어간다(직접 빌드한 쏙은 업데이트 확인 안 함).

## 작업 방법

- **기준 소스는 이 GitHub 저장소(main).** 고친 뒤 main에 커밋·푸시한다. 커밋 메시지는 한국어로 짧게.
- 이 클라우드 환경에는 .NET·윈도우가 없어 빌드·실행을 못 한다. 그래서:
  - 고칠 때는 컴파일 오류가 없도록 특히 꼼꼼히 본다.
  - 사용자 PC가 연결돼 있으면 같은 파일을 `C:\Users\jossa\Documents\Ssok`에도 써 주고, 사용자에게 `build.cmd`를 돌려 달라고 한다. 결과는 `build-log.txt`·`build-selftest.txt`를 직접 읽어 확인 (`BUILD-RESULT: OK`, `SELFTEST: OK`).
  - PC 연결이 없으면 GitHub Actions의 **release 워크플로를 수동 실행(workflow_dispatch, 버전 입력)** 하면 배포 없이 빌드·시험만 돈다. 결과 파일은 실행 화면의 artifacts.
  - `.github` 폴더는 PC 쪽에 쓸 수 없게 막혀 있다 → GitHub에만 있다.
- 새 기능에는 `tests/Ssok.Core.Tests/Program.cs`(핵심)나 `SelfTest.cs`(실제 윈도우) 시험을 함께 넣는다.

## 새 버전 배포 순서

1. `src/Ssok.App/Ssok.App.csproj`의 `<Version>`을 올린다 (예: 0.4.0 → 0.4.1). README의 "지금 되는 것"도 필요하면 고친다.
2. main에 푸시. (가능하면 PC에서 `build.cmd`로 먼저 확인)
3. **태그는 여기서 푸시할 수 없다(프록시가 태그 푸시를 막음).** 사용자가 GitHub에서 Releases → Draft a new release → 태그 `v0.4.1` 새로 만들기 → 제목 `쏙 0.4.1` → **Publish release**를 누른다. (built-in 브라우저로 태그 입력까지 도와줄 수 있지만, 공개 단추 Publish는 사용자가 누른다.)
4. release 워크플로가 설치판·무설치판·`update.json`(서명)·`SHA256SUMS.txt`를 그 Release에 올린다. 진행은 Actions 탭에서 확인.
5. 이미 설치판을 쓰는 사람은 쏙 오른쪽 아래 "새 버전 받기"로 업데이트된다.

## 남은 일

- SignPath Foundation 무료 코드서명: 2026-10-24 신청 → 같은 날 거절(공개 인지도 부족: 별·포크·외부 글·커뮤니티 언급 등). 알려진 뒤 재신청 가능. 그동안은 서명 없이 배포하고 홈페이지에서 SmartScreen 넘기는 법 안내. 재승인 시: 승인되면 저장소 변수 `SIGNPATH_ORGANIZATION_ID`, 비밀값 `SIGNPATH_API_TOKEN` 넣기. 지금 워크플로는 설치판만 서명 — 무설치판 속 Ssok.exe도 서명하려면 SignPath 아티팩트 설정과 워크플로 순서를 조정해야 한다.
- 속도 개선 후보(사용자에게 설명만 하고 보류 중): 여러 파일 동시 처리, PDF 크기 어림으로 qpdf 실행 횟수 줄이기, 문서 속 그림 병렬 압축, PDF 쪽 병렬 렌더링.

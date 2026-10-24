# 쏙 배포하기

처음 한 번만 하는 준비(1~3)와, 새 버전을 낼 때마다 하는 일(4)로 나뉩니다.

## 1. GitHub에 쏙 저장소 만들기 (처음 한 번)

1. https://github.com 에 로그인합니다.
2. 오른쪽 위 **+ → New repository**
   - Repository name: `ssok`
   - **Public** 선택 (무료 코드 서명은 공개 저장소만 받을 수 있습니다)
   - 나머지는 비워 두고 **Create repository**
3. 이 폴더의 소스를 그 저장소에 올립니다. (Claude에게 "저장소 만들었어"라고 알려 주면 올려 드립니다.)

## 2. 업데이트 서명 키 넣기 (처음 한 번)

자동 업데이트가 가짜 파일을 받지 않도록, 배포할 때 비밀 키로 서명합니다.

1. `tools\make-update-key.cmd`를 두 번 눌러 실행합니다.
2. 문서 폴더에 생긴 `쏙-업데이트-비밀키.txt`를 엽니다.
3. GitHub 저장소 → **Settings → Secrets and variables → Actions → New repository secret**
   - Name: `SSOK_UPDATE_KEY`
   - Secret: 파일 안의 긴 한 줄 전체
4. 이 파일은 USB 등에 따로 보관하세요. 잃어버리면 이미 설치된 쏙에 자동 업데이트를 보낼 수 없습니다.

## 3. 무료 코드 서명 신청 (처음 한 번, 승인까지 보통 몇 주)

서명이 없으면 처음 실행할 때 윈도우가 "Windows의 PC 보호" 창을 띄웁니다. (**추가 정보 → 실행**을 누르면 실행됩니다.)
오픈소스 프로젝트는 SignPath Foundation에서 무료로 서명을 받을 수 있습니다.

1. 첫 버전(아래 4번)을 먼저 GitHub Releases에 올려 둡니다. (신청할 때 배포 중인 프로젝트여야 합니다)
2. https://signpath.org/apply 에서 신청서를 냅니다.
   - Project: 쏙 (Ssok) / 저장소 주소
   - License: MIT
   - 설명 예: "Windows app for teachers that shrinks PDF, HWP/HWPX and photo files to a target size, entirely offline. Built with GitHub Actions from this public repository."
   - 코드 서명 정책은 README의 '코드 서명 정책' 부분을 가리키면 됩니다.
3. 승인되면 SignPath에서 받은 값을 넣습니다.
   - 저장소 변수(Variables) `SIGNPATH_ORGANIZATION_ID`
   - 저장소 비밀값(Secrets) `SIGNPATH_API_TOKEN`
   - SignPath 프로젝트 이름은 `ssok`, 서명 정책 이름은 `release-signing`으로 만듭니다.
4. 그다음 배포부터 설치 파일이 자동으로 서명됩니다.

## 4. 새 버전 배포하기 (버전마다)

1. `src\Ssok.App\Ssok.App.csproj`의 `<Version>`을 올립니다. (예: 0.4.0 → 0.4.1)
2. `build.cmd`로 이 PC에서 먼저 만들어 보고 `BUILD-RESULT: OK`인지 확인합니다.
3. GitHub에 올리고 `v0.4.1` 태그를 붙입니다. (Claude에게 "0.4.1 배포해 줘"라고 하면 됩니다.)
4. 몇 분 뒤 저장소의 **Releases**에 설치판·무설치판이 올라옵니다.
   - 이미 설치판을 쓰는 선생님은 쏙 오른쪽 아래에 **새 버전 받기**가 보입니다.
   - 처음 쓰시는 선생님께는 `https://github.com/계정이름/ssok/releases/latest` 주소를 알려 드리세요.

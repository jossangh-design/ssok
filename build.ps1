# 쏙 빌드 스크립트 - build.cmd를 두 번 눌러 실행하세요.
# -CI: GitHub Actions에서 쓸 때 (설치 창·엔터 기다리기 없음, 실패하면 종료 코드 1)
# -Version 0.4.0: 버전을 정해서 만들 때 (없으면 프로젝트 파일의 버전)
# 환경 변수 SSOK_UPDATE_REPO, SSOK_UPDATE_PUBLIC_KEY가 있으면 업데이트 확인 기능이 켜진 쏙을 만든다.
param([switch]$CI, [string]$Version = '')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$log = Join-Path $root 'build-log.txt'
Start-Transcript -Path $log -Force | Out-Null
$failed = $false

function Step($text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }

# PDF 처리 부품 qpdf (Apache-2.0). 버전과 지문을 고정해 바뀐 파일은 쓰지 않는다.
$QpdfVersion = '12.4.2'
$QpdfSha256 = 'DB87077E683630C1217E0E8F9A20A9749D952AB676E881C3689187763A5DE25D'
$QpdfUrl = "https://github.com/qpdf/qpdf/releases/download/v$QpdfVersion/qpdf-$QpdfVersion-msvc64.zip"

try {
    Step '.NET 10 SDK 확인'
    $sdks = ''
    try { $sdks = (& dotnet --list-sdks) -join "`n" } catch { }
    if ($sdks -notmatch '(?m)^10\.') {
        if ($CI) { throw '.NET 10 SDK가 없습니다.' }
        Write-Host '.NET 10 SDK가 없어 설치합니다. 설치 창이 뜨면 "예"를 눌러 주세요.' -ForegroundColor Yellow
        winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
        $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
        $sdks = (& dotnet --list-sdks) -join "`n"
        if ($sdks -notmatch '(?m)^10\.') { throw '.NET 10 SDK 설치를 확인하지 못했습니다. PC를 다시 켠 뒤 build.cmd를 다시 실행해 주세요.' }
    }
    Write-Host $sdks
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'

    Step "qpdf $QpdfVersion 준비"
    $cache = Join-Path $root '.cache'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $zip = Join-Path $cache "qpdf-$QpdfVersion-msvc64.zip"
    if (-not (Test-Path $zip)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $QpdfUrl -OutFile $zip -UseBasicParsing
    }
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($hash -ne $QpdfSha256) {
        Remove-Item $zip -Force
        throw "qpdf 파일 지문이 다릅니다 ($hash). 받은 파일을 지웠습니다."
    }
    $qpdfDir = Join-Path $cache "qpdf-$QpdfVersion-msvc64"
    if (-not (Test-Path $qpdfDir)) { Expand-Archive -Path $zip -DestinationPath $cache -Force }
    $qpdfBin = Join-Path $qpdfDir 'bin'
    $env:SSOK_QPDF = Join-Path $qpdfBin 'qpdf.exe'
    Write-Host "qpdf: $env:SSOK_QPDF"

    Step '핵심 기능 시험'
    & dotnet run --project tests/Ssok.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw '시험에서 실패한 항목이 있습니다.' }

    Step '쏙 만들기'
    $dist = Join-Path $root 'dist'
    # 이전에 만든 쏙이 켜져 있으면 파일을 지울 수 없으므로 먼저 확인한다.
    $running = Get-Process -Name 'Ssok' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) }
    if ($running -and $CI) { throw '쏙이 켜져 있습니다.' }
    if ($running) {
        Write-Host '쏙이 켜져 있습니다. 쏙 창을 닫은 뒤 엔터를 눌러 주세요.' -ForegroundColor Yellow
        Read-Host | Out-Null
        $running = Get-Process -Name 'Ssok' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) }
        if ($running) { throw '쏙이 아직 켜져 있습니다. 쏙을 닫고 build.cmd를 다시 실행해 주세요.' }
    }
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    $extra = @()
    if ($Version) { $extra += "-p:Version=$Version" }
    if ($env:SSOK_UPDATE_REPO -and $env:SSOK_UPDATE_PUBLIC_KEY) {
        $extra += "-p:SsokUpdateRepo=$($env:SSOK_UPDATE_REPO)"
        $extra += "-p:SsokUpdatePublicKey=$($env:SSOK_UPDATE_PUBLIC_KEY)"
        Write-Host "업데이트 확인 켬: $($env:SSOK_UPDATE_REPO)"
    }
    & dotnet publish src/Ssok.App/Ssok.App.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none @extra -o $dist
    if ($LASTEXITCODE -ne 0) { throw '빌드에 실패했습니다.' }

    # qpdf 실행에 필요한 파일만 넣는다 (qpdf.exe, qpdf30.dll, MSVC 런타임)
    $qpdfOut = Join-Path $dist 'qpdf'
    New-Item -ItemType Directory -Force -Path $qpdfOut | Out-Null
    Get-ChildItem $qpdfBin -File | Where-Object { $_.Name -notin @('fix-qdf.exe', 'zlib-flate.exe') } |
        Copy-Item -Destination $qpdfOut
    $licOut = Join-Path $dist 'licenses'
    New-Item -ItemType Directory -Force -Path $licOut | Out-Null
    Copy-Item (Join-Path $root 'licenses\*') $licOut
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dist 'LICENSE.txt')
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $dist

    Step '실제 윈도우에서 자가 시험'
    $report = Join-Path $root 'build-selftest.txt'
    if (Test-Path $report) { Remove-Item $report -Force }
    $exe = Join-Path $dist 'Ssok.exe'
    $fixtures = Join-Path $root 'tests\fixtures'
    $p = Start-Process -FilePath $exe -ArgumentList @('--self-test', "`"$fixtures`"", "`"$report`"") -Wait -PassThru
    if (Test-Path $report) { Get-Content $report -Encoding UTF8 | Write-Host }
    if ($p.ExitCode -ne 0) { throw "자가 시험에서 실패한 항목이 있습니다 (종료 코드 $($p.ExitCode))." }

    Step '배포 파일 만들기'
    $ver = if ($Version) { $Version } else { ((Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', '') }
    $release = Join-Path $root 'release'
    if (Test-Path $release) { Remove-Item $release -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $release | Out-Null
    # 무설치판: 압축을 풀고 Ssok.exe를 바로 실행
    $portable = Join-Path $release "Ssok-Portable-$ver.zip"
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $portable -CompressionLevel Optimal
    Write-Host "무설치판: $portable"
    # 설치판: Inno Setup 6이 있으면 만든다
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
              "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($iscc) {
        & $iscc "/DAppVersion=$ver" "/DSourceDir=$dist" "/DOutputDir=$release" (Join-Path $root 'installer\Ssok.iss') | Out-Host
        if ($LASTEXITCODE -ne 0) { throw '설치 파일을 만들지 못했습니다.' }
        Write-Host "설치판: $(Join-Path $release "Ssok-Setup-$ver.exe")"
    } elseif ($CI) {
        throw 'Inno Setup(ISCC.exe)을 찾지 못했습니다.'
    } else {
        Write-Host '설치판은 건너뜀: 이 PC에 Inno Setup 6이 없습니다. (공식 배포 파일은 GitHub에서 자동으로 만듭니다)' -ForegroundColor Yellow
    }

    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash
    $size = [math]::Round(((Get-ChildItem $dist -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Step '완료'
    Write-Host "결과 폴더: $dist ($size MB)"
    Write-Host "Ssok.exe SHA-256: $hash"
    Write-Host 'BUILD-RESULT: OK'
}
catch {
    Write-Host ''
    Write-Host "BUILD-RESULT: FAIL - $($_.Exception.Message)" -ForegroundColor Red
    $failed = $true
}
finally {
    Stop-Transcript | Out-Null
}
if ($CI) { if ($failed) { exit 1 } else { exit 0 } }
Write-Host ''
Read-Host '엔터를 누르면 창이 닫힙니다'

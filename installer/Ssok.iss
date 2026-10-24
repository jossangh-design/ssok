; 쏙 설치 파일 (Inno Setup 6). 관리자 권한 없이 사용자 폴더에 설치한다.
; 빌드: ISCC.exe /DAppVersion=0.4.0 /DSourceDir=..\dist /DOutputDir=..\release installer\Ssok.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist"
#endif
#ifndef OutputDir
  #define OutputDir "..\release"
#endif

[Setup]
AppId={{6E0C2B7A-5D1F-4C8E-9B3A-2F7D4E1A9C55}
AppName=쏙
AppVersion={#AppVersion}
AppVerName=쏙 {#AppVersion}
AppPublisher=Ssok contributors
AppPublisherURL=https://github.com
VersionInfoVersion={#AppVersion}
VersionInfoDescription=쏙 설치
; 관리자 권한 없이 %LocalAppData%\Programs\Ssok 에 설치 (쏙의 업데이트 기능이 이 위치를 설치판으로 알아본다)
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Ssok
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName=쏙
UninstallDisplayName=쏙
UninstallDisplayIcon={app}\Ssok.exe
SetupIconFile=..\src\Ssok.App\Assets\ssok.ico
OutputDir={#OutputDir}
OutputBaseFilename=Ssok-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; 업데이트할 때 켜져 있는 쏙을 닫는다
CloseApplications=force
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 쏙 바로가기 만들기"; GroupDescription: "바로가기:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\쏙"; Filename: "{app}\Ssok.exe"
Name: "{userdesktop}\쏙"; Filename: "{app}\Ssok.exe"; Tasks: desktopicon

[Run]
; 처음 설치: 마지막 화면에서 '쏙 실행' 선택
Filename: "{app}\Ssok.exe"; Description: "쏙 실행"; Flags: nowait postinstall skipifsilent
; 쏙 안에서 업데이트(조용히 설치)했을 때: 끝나면 쏙을 다시 켠다
Filename: "{app}\Ssok.exe"; Flags: nowait skipifnotsilent

[UninstallDelete]
; 쏙이 만든 임시 작업 폴더만 지운다 (사용자 파일은 건드리지 않음)
Type: filesandordirs; Name: "{localappdata}\Temp\Ssok"

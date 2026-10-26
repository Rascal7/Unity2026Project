; Inno Setup 스크립트 - Unity2026 Co-op Platformer
; 최신 Inno Setup 6 / Inno Setup 7 완벽 호환
; 공식 문서: https://jrsoftware.org/isinfo.php

#define MyAppName "Unity2026 Co-op Platformer"
#define MyAppVersion "beta1.0"
#define MyAppPublisher "Unity2026 Team"
#define MyAppExeName "Unity2026Project.exe"

[Setup]
; 고유 애플리케이션 GUID (추후 새 버전 배포 시 기존 버전을 인식하고 덮어씌움)
AppId={{E14D43F9-28BC-4F66-9B3B-8C459037BE1B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; 설치 마법사 출력 폴더 및 파일명 (Builds/Installer/Unity2026_Setup_beta1.0.exe)
OutputDir=..\Builds\Installer
OutputBaseFilename=Unity2026_Setup_{#MyAppVersion}

; 압축 설정 (LZMA2 Ultra64 최대 압축)
Compression=lzma2/ultra64
SolidCompression=yes

; 64비트 전용 시스템 호환 설정 (최신 Inno Setup 6/7 권장 규격)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 권한 설정: 일반 사용자 설치 및 관리자 권한 승격 다이얼로그 지원
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; 실행 중인 게임 감지 시 자동 종료 안내
CloseApplications=yes

; UI 및 표시 정보
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} ({#MyAppVersion})

; 실행 파일 속성(자세히 보기) 메타데이터
VersionInfoVersion=1.0.0.0
VersionInfoTextVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Builds/Windows 폴더 안의 모든 파일(Data, DLL, StreamingAssets 음성 모델 등)을 통째로 포함
Source: "..\Builds\Windows\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Builds\Windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 설치 완료 후 게임 바로 실행 옵션
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; ============================================================================
;  Trag - Git Client - installer
;
;  Build (Inno Setup 6.3 or newer), the publish and the setup in one step:
;    powershell -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1 -Version 1.0.0
;
;  Packs the NativeAOT publish in artifacts\publish\win-x64. Trag.exe and its native libraries need only the Universal
;  C Runtime, which Windows 10 and later carry, so no runtime is installed beside them.
;
;  Trag installs for the current user, into %LOCALAPPDATA%\Programs\Trag, without administrator rights, so an update
;  needs none either. The install mode must not change after the first release: a changed mode installs a second copy
;  instead of updating the first.
; ============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName        "Trag"
#define MyAppTitle       "Trag - Git Client"
#define MyAppPublisher   "Finity Software"
#define MyAppURL         "https://trag.sh"
#define MyAppExeName     "Trag.exe"
#define PublishDir       "..\artifacts\publish\win-x64"

; The version Windows reads from the setup file is numeric only (x.y.z): a suffix such as 1.2.0-beta is left out there.
#if Pos("-", MyAppVersion) > 0
  #define MyFileVersion Copy(MyAppVersion, 1, Pos("-", MyAppVersion) - 1)
#else
  #define MyFileVersion MyAppVersion
#endif

[Setup]
; Trag's identity across every future version. Never change it after the first release: an update would install as a
; separate copy instead of replacing the one installed.
AppId={{346A830D-0985-49F3-B9CB-A4CDAD80B649}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppTitle} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=https://github.com/finitysoftware/trag/issues
AppUpdatesURL=https://downloads.finitysoftware.io/trag
AppCopyright=© 2026 {#MyAppPublisher}
VersionInfoVersion={#MyFileVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppTitle} Setup
VersionInfoProductName={#MyAppTitle}

PrivilegesRequired=lowest
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename=Trag-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\src\Trag.Desktop\Assets\trag-icon.ico
; Trag's own license, which the user accepts before anything is installed.
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppTitle}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Symbols do not go to users: they only make the installation larger.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppTitle}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppTitle}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

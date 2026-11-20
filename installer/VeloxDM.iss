; Inno Setup script for VeloxDM.
; Build:  ISCC.exe installer\VeloxDM.iss   (after "dotnet publish ... -o publish", see build-installer.ps1)

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppName "VeloxDM"
#define AppExe "VeloxDM.exe"

[Setup]
AppId={{6B0E3C7A-2F4D-4E8B-9C51-7A1D2E5F9B30}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=VeloxDM contributors
AppComments=Open-source multi-connection download manager
VersionInfoVersion={#AppVersion}
; Per-user install: no administrator rights needed (user may still choose all-users).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\VeloxDM.App\Assets\velox.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=VeloxDM-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Ask a running VeloxDM to close before upgrading/uninstalling.
AppMutex=VeloxDM.SingleInstance
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "videotools"; Description: "Download video tools now: yt-dlp, FFmpeg, Deno (free, open source, about 150 MB, needs internet)"; GroupDescription: "YouTube and video sites:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Start VeloxDM in the notification area when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
    ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
; Downloads the tools from their official GitHub releases into %LOCALAPPDATA%\VeloxDM\tools.
; They are fetched at install time rather than bundled, so the installer stays small and the tools are current.
Filename: "{app}\{#AppExe}"; Parameters: "--setup-tools"; StatusMsg: "Downloading video tools (yt-dlp, FFmpeg, Deno)..."; \
    Tasks: videotools; Flags: waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; User data (%LOCALAPPDATA%\VeloxDM: download list, settings, video tools) is kept on uninstall
; so a reinstall keeps your history. Delete that folder manually to remove everything.

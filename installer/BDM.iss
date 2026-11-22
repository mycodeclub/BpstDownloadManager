; Inno Setup script for BPST Download Manager (BDM) 2.x - Windows.
; Build: .\build-installer.ps1  (publishes the MAUI app to publish\windows, runs the self-test, then compiles this)

#ifndef AppVersion
  #define AppVersion "2.1.0"
#endif
#define AppName "BPST Download Manager"
#define AppShort "BDM"
#define AppExe "BDM.exe"

[Setup]
; Same AppId as VeloxDM / BpstVideoDownloader 0.x, so installing 2.0 upgrades an existing installation.
AppId={{6B0E3C7A-2F4D-4E8B-9C51-7A1D2E5F9B30}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=BitProSoftTech
AppPublisherURL=https://bitprosofttech.com
AppSupportURL=https://bitprosofttech.com
AppUpdatesURL=https://bitprosofttech.com
AppContact=+(91)-82-9910-1616
AppComments=Free open-source download manager by BitProSoftTech
AppCopyright=Copyright (c) 2026 BitProSoftTech
VersionInfoVersion={#AppVersion}
VersionInfoCompany=BitProSoftTech
VersionInfoDescription={#AppName} Setup
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppShort}
DefaultGroupName={#AppShort}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\Bdm.App\Resources\AppIcon\bdm.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=BDM-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Close a running BDM (or the older VeloxDM / BpstVideoDownloader) before installing.
AppMutex=BDM.SingleInstance,BpstVideoDownloader.SingleInstance,VeloxDM.SingleInstance
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to [name] Setup
WelcomeLabel2=This will install [name/ver] on your computer.%n%nBDM is a free, open-source download manager by BitProSoftTech: fast multi-connection downloads, YouTube and streaming video, torrents, and Focus mode.%n%nWebsite: bitprosofttech.com%nPhone: +(91)-82-9910-1616

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "magnet"; Description: "Open magnet: links and .torrent files with BDM"; GroupDescription: "Torrents:"

[Files]
Source: "..\publish\windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[InstallDelete]
; Files of the 0.x WPF app that 2.0 no longer uses.
Type: files; Name: "{app}\VeloxDM.exe"
Type: files; Name: "{app}\BpstVideoDownloader.exe"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "BDM by BitProSoftTech"
Name: "{autoprograms}\BitProSoftTech website"; Filename: "https://bitprosofttech.com"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Per-user handlers for magnet: links and .torrent files (only if chosen).
Root: HKCU; Subkey: "Software\Classes\magnet"; ValueType: string; ValueData: "URL:Magnet link"; Flags: uninsdeletekey; Tasks: magnet
Root: HKCU; Subkey: "Software\Classes\magnet"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Tasks: magnet
Root: HKCU; Subkey: "Software\Classes\magnet\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: magnet
Root: HKCU; Subkey: "Software\Classes\.torrent"; ValueType: string; ValueData: "BDM.torrent"; Flags: uninsdeletevalue; Tasks: magnet
Root: HKCU; Subkey: "Software\Classes\BDM.torrent"; ValueType: string; ValueData: "Torrent file"; Flags: uninsdeletekey; Tasks: magnet
Root: HKCU; Subkey: "Software\Classes\BDM.torrent\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: magnet

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

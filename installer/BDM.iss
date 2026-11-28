; Inno Setup script for BPST Download Manager (BDM) 2.x - Windows.
;
; Build: .\build-installer.ps1  (publishes the MAUI app to publish\windows, runs the self-test, then compiles this)
;
; Updating: this Setup is also the updater. If BDM (or VeloxDM / BpstVideoDownloader 0.x) is already installed
; it finds it by AppId, says "update from x to y", closes the running app, installs into the same folder
; with the same options, and keeps downloads and settings (%LOCALAPPDATA%\BDM is never touched).
; Unattended update: BDM-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART  (a downgrade is refused when silent).

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
; Update in place: reuse the installed folder, Start-menu group and chosen tasks; skip the folder page.
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousPrivileges=yes
DisableDirPage=auto

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

[Code]
const
  UninstKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B0E3C7A-2F4D-4E8B-9C51-7A1D2E5F9B30}_is1';

var
  InstalledVersion: String;

{ Installed version from the uninstall entry (per-user first, then per-machine), or '' if not installed. }
function GetInstalledVersion(): String;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, UninstKey, 'DisplayVersion', Result) then
    if not RegQueryStringValue(HKLM64, UninstKey, 'DisplayVersion', Result) then
      RegQueryStringValue(HKLM32, UninstKey, 'DisplayVersion', Result);
end;

{ Next number from a dotted version string ('2.10.1' -> 2, then 10, then 1; missing parts are 0). }
function NextPart(var S: String): Integer;
var
  P: Integer;
begin
  P := Pos('.', S);
  if P = 0 then begin
    Result := StrToIntDef(S, 0);
    S := '';
  end else begin
    Result := StrToIntDef(Copy(S, 1, P - 1), 0);
    Delete(S, 1, P);
  end;
end;

{ <0 if A is older than B, 0 if equal, >0 if newer. }
function CompareVersions(A, B: String): Integer;
var
  I, X, Y: Integer;
begin
  Result := 0;
  for I := 1 to 4 do begin
    X := NextPart(A);
    Y := NextPart(B);
    if X <> Y then begin
      Result := X - Y;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  InstalledVersion := GetInstalledVersion();
  if (InstalledVersion <> '') and (CompareVersions(InstalledVersion, '{#AppVersion}') > 0) then begin
    { Never silently replace a newer BDM with an older one. }
    if WizardSilent() then begin
      Log('BDM ' + InstalledVersion + ' is newer than this Setup ({#AppVersion}); nothing to do.');
      Result := False;
    end else
      Result := MsgBox('BDM ' + InstalledVersion + ' is already installed, which is newer than this Setup ({#AppVersion}).'
        + #13#10#13#10 + 'Install the older version anyway?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
end;

procedure InitializeWizard();
var
  Keep: String;
begin
  if InstalledVersion = '' then Exit;
  Keep := #13#10#13#10 + 'Your downloads, download list and settings are kept. If BDM is running, Setup closes it first.';
  if CompareVersions(InstalledVersion, '{#AppVersion}') = 0 then begin
    WizardForm.WelcomeLabel1.Caption := 'Repair {#AppName}';
    WizardForm.WelcomeLabel2.Caption := '{#AppName} {#AppVersion} is already installed. Setup will reinstall it in the same folder.' + Keep;
  end else begin
    WizardForm.WelcomeLabel1.Caption := 'Update {#AppName}';
    WizardForm.WelcomeLabel2.Caption := 'Setup will update {#AppName} from version ' + InstalledVersion + ' to {#AppVersion}.' + Keep;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (InstalledVersion <> '') and (CurPageID = wpReady) then
    WizardForm.NextButton.Caption := 'Update';
end;

; AION2 Pet Tracker: Windows installer (Inno Setup 6.3 or newer, https://jrsoftware.org).
; Built by build-installer.ps1:
;   ISCC /DAppVer=<x.y.z> /DAppExePath=<AION2PetTracker.exe> /O<output folder> AION2PetTracker.iss
; Installs for the current user only (no administrator prompt) into %LocalAppData%\Programs\AION2 Pet Tracker.
; Files an installer writes carry no "downloaded from the internet" mark, so after this one Setup prompt Windows
; SmartScreen never asks again when the tracker starts.
; Settings and pet progress stay in %AppData%\AION2PetTracker; uninstall asks before deleting them.
; Based on the installer of AION2 DPS Meter (GPL-3.0).

#ifndef AppVer
  #define AppVer "0.0.0"
#endif
#ifndef AppExePath
  #error Pass /DAppExePath=<the published AION2PetTracker.exe>
#endif
#define AppName "AION2 Pet Tracker"
#define AppExe "AION2PetTracker.exe"
; Identifies the program for upgrades and uninstall: never change it (the app looks for this uninstall key).
#define AppGuid "{27D8681D-DA79-4717-B70E-F2FF09CD0A77}"

[Setup]
AppId={{#AppGuid}
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
AppPublisher={#AppName}
AppPublisherURL=https://github.com/ysaatci/aion2-pet-tracker
AppSupportURL=https://github.com/ysaatci/aion2-pet-tracker/issues
AppUpdatesURL=https://github.com/ysaatci/aion2-pet-tracker/releases
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=AION2PetTracker-Setup-v{#AppVer}
SetupIconFile=..\src\AionMeter.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; An update closes the running tracker (only the copy installed here) through Restart Manager.
CloseApplications=force
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
NpcapTitle=Npcap is needed
NpcapSubtitle=A free capture driver: the tracker sees the game's traffic through it.
NpcapText=Npcap is not installed on this computer yet. Without it the tracker starts but cannot see the game.%n%n1. Click "Open npcap.com" and download the Npcap installer.%n2. Run it and keep the default options.%n3. Come back here and click Next.%n%nNpcap cannot be bundled with this installer: its licence does not allow that.
NpcapOpen=Open npcap.com
NpcapRecheck=Check again
NpcapFound=Npcap found, you are all set.
NpcapMissing=Npcap not found yet.
NpcapContinue=Npcap is still not installed. Install the tracker anyway?%n%nThe tracker will remind you about Npcap when it starts.
CloseRunning=AION2 Pet Tracker is running. Close it and continue?
RemoveUserData=Also delete your settings and pet progress?%n%n%1

[Files]
Source: "{#AppExePath}"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Game data unpacked by the program itself (a fresh copy is unpacked per version).
Type: filesandordirs; Name: "{localappdata}\AION2PetTracker\data"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\AION2PetTracker"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Check: DesktopShortcutWanted

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; In-app update: the tracker runs this installer with /SILENT /RELAUNCH=1 and exits; start the new version afterwards.
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait; Check: RelaunchAfterUpdate

[Code]
var
  NpcapPage: TWizardPage;
  NpcapStatus: TNewStaticText;
  FilesInstalled: Boolean;

// Same test as the tracker itself (LiveCapture.IsNpcapInstalled). Setup runs in 64-bit mode, so {sys} is System32.
function NpcapInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) or FileExists(ExpandConstant('{sys}\wpcap.dll'));
end;

procedure UpdateNpcapStatus;
begin
  if NpcapInstalled then
    NpcapStatus.Caption := CustomMessage('NpcapFound')
  else
    NpcapStatus.Caption := CustomMessage('NpcapMissing');
end;

procedure OpenNpcapClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'https://npcap.com/#download', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure RecheckClick(Sender: TObject);
begin
  UpdateNpcapStatus;
end;

// A page that explains Npcap; skipped when Npcap is already there.
procedure InitializeWizard;
var
  Info: TNewStaticText;
  OpenButton, RecheckButton: TNewButton;
begin
  NpcapPage := CreateCustomPage(wpLicense, CustomMessage('NpcapTitle'), CustomMessage('NpcapSubtitle'));

  Info := TNewStaticText.Create(NpcapPage);
  Info.Parent := NpcapPage.Surface;
  Info.AutoSize := False;
  Info.WordWrap := True;
  Info.Width := NpcapPage.SurfaceWidth;
  Info.Caption := CustomMessage('NpcapText');
  Info.AdjustHeight;

  OpenButton := TNewButton.Create(NpcapPage);
  OpenButton.Parent := NpcapPage.Surface;
  OpenButton.Caption := CustomMessage('NpcapOpen');
  OpenButton.Top := Info.Top + Info.Height + ScaleY(14);
  OpenButton.Width := ScaleX(160);
  OpenButton.Height := WizardForm.NextButton.Height;
  OpenButton.OnClick := @OpenNpcapClick;

  RecheckButton := TNewButton.Create(NpcapPage);
  RecheckButton.Parent := NpcapPage.Surface;
  RecheckButton.Caption := CustomMessage('NpcapRecheck');
  RecheckButton.Top := OpenButton.Top;
  RecheckButton.Left := OpenButton.Left + OpenButton.Width + ScaleX(10);
  RecheckButton.Width := ScaleX(140);
  RecheckButton.Height := OpenButton.Height;
  RecheckButton.OnClick := @RecheckClick;

  NpcapStatus := TNewStaticText.Create(NpcapPage);
  NpcapStatus.Parent := NpcapPage.Surface;
  NpcapStatus.Top := OpenButton.Top + OpenButton.Height + ScaleY(14);
  NpcapStatus.Font.Style := [fsBold];
  UpdateNpcapStatus;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (NpcapPage <> nil) and (PageID = NpcapPage.ID) and NpcapInstalled;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (NpcapPage <> nil) and (CurPageID = NpcapPage.ID) then
    UpdateNpcapStatus;
end;

// Silent installs (in-app updates) also pass through here: never stop them on the Npcap page.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (NpcapPage <> nil) and (CurPageID = NpcapPage.ID) and not NpcapInstalled and not WizardSilent then
    Result := MsgBox(CustomMessage('NpcapContinue'), mbConfirmation, MB_YESNO) = IDYES;
end;

// A first install puts a shortcut on the desktop. An update keeps what the player did with it: a shortcut they
// deleted is not put back.
function DesktopShortcutWanted: Boolean;
begin
  Result := not RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1', 'UninstallString')
            or FileExists(ExpandConstant('{autodesktop}\{#AppName}.lnk'));
end;

function RelaunchAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    FilesInstalled := True;
end;

// The tracker closed itself for this in-app update. If the update did not go through, start the copy that is still
// there, so the player is not left without it.
procedure DeinitializeSetup;
var
  ExitCode: Integer;
begin
  if RelaunchAfterUpdate and not FilesInstalled then
  try
    if FileExists(ExpandConstant('{app}\{#AppExe}')) then
      Exec(ExpandConstant('{app}\{#AppExe}'), '', '', SW_SHOWNORMAL, ewNoWait, ExitCode);
  except
    // {app} is unknown when Setup stopped before choosing a folder: nothing to start then.
  end;
end;

// Uninstall: close the copy that runs from this folder (a portable copy elsewhere is left alone). Returns False when
// the user prefers to keep it running.
function CloseRunningTracker(Ask: Boolean): Boolean;
var
  Locator, Wmi, Procs, Proc: Variant;
  I, Pid, ExitCode: Integer;
  Dir, Path: String;
  Asked: Boolean;
begin
  Result := True;
  Asked := False;
  Dir := AddBackslash(ExpandConstant('{app}'));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Wmi.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''{#AppExe}''');
    for I := 0 to Procs.Count - 1 do
    begin
      Proc := Procs.ItemIndex(I);
      if Result and not VarIsNull(Proc.ExecutablePath) then
      begin
        Path := Proc.ExecutablePath;
        if CompareText(Copy(Path, 1, Length(Dir)), Dir) = 0 then
        begin
          if Ask and not Asked then
          begin
            Asked := True;
            Result := MsgBox(CustomMessage('CloseRunning'), mbConfirmation, MB_YESNO) = IDYES;
          end;
          if Result then
          begin
            Pid := Proc.ProcessId;
            Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /PID ' + IntToStr(Pid), '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
          end;
        end;
      end;
    end;
  except
    // No WMI: uninstall goes on and reports any file it could not remove, as usual.
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := CloseRunningTracker(not UninstallSilent);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    Data := ExpandConstant('{userappdata}\AION2PetTracker');
    if DirExists(Data) and
       (MsgBox(FmtMessage(CustomMessage('RemoveUserData'), [Data]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(Data, True, True, True);
  end;
end;

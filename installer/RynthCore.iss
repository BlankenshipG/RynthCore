; RynthCore + RynthSuite Installer
; Build with: Build-Installer.ps1 (requires Inno Setup 6)
; Source: https://jrsoftware.org/isdl.php
;
; NOTE: No ISPP (#define / {#...}) macros are used in this file.
; Build-Installer.ps1 replaces the AppVersion placeholder via text substitution
; before invoking ISCC, so the preprocessor has nothing to expand.
; AppVersion placeholder — replaced by Build-Installer.ps1 before ISCC runs:
; APPVERSION_PLACEHOLDER=0.0.0

[Setup]
AppId={{A8B4C2D1-E3F5-4678-9ABC-DEF012345678}
AppName=RynthCore
AppVersion=0.0.0
AppPublisher=RynthCore
AppPublisherURL=https://github.com/tombohar/RynthCore
DefaultDirName=C:\Games\RynthCore
DisableDirPage=no
DefaultGroupName=RynthCore
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=RynthBundle-Setup
SetupIconFile=..\src\RynthCore.App.Avalonia\LogoCore.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Run as lowest privilege; Windows will prompt for elevation only when
; the chosen install folder requires it (e.g. C:\Games\).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 32-bit app; works on 32/64-bit Windows
ArchitecturesAllowed=x86 x64 arm64
ArchitecturesInstallIn64BitMode=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

; Program files
[Files]
; Installs the entire staging layout (launcher, Runtime\, Tools\, etc.)
Source: "staging\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Data directories (created once; never removed on uninstall)
[Dirs]
Name: "C:\Games\RynthSuite\RynthAi";                             Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\NavProfiles";                 Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\LootProfiles";                Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\MetaFiles";                   Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\MetaProfiles";                Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\SettingsProfiles";            Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\SettingsProfiles\ACEmulator"; Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\MonsterProfiles";             Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\LuaScripts";                  Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\Logs";                        Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\pvars";                       Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\ItemGiver";                   Flags: uninsneveruninstall

; Shortcuts
[Icons]
; Start Menu
Name: "{group}\RynthCore"; Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Comment: "Launch RynthCore and inject into Asheron's Call"
Name: "{group}\RynthCore Injector (CLI)"; Filename: "{app}\Runtime\RynthCore.Injector.exe"; WorkingDir: "{app}\Runtime"; Check: FileExists(ExpandConstant('{app}\Runtime\RynthCore.Injector.exe'))
Name: "{group}\RynthCore Loot Editor"; Filename: "{app}\Tools\LootEditor\RynthCore.LootEditor.exe"; WorkingDir: "{app}\Tools\LootEditor"; Check: FileExists(ExpandConstant('{app}\Tools\LootEditor\RynthCore.LootEditor.exe'))
Name: "{group}\RynthCore Monster Editor"; Filename: "{app}\Tools\MonsterEditor\RynthCore.MonsterEditor.exe"; WorkingDir: "{app}\Tools\MonsterEditor"; Check: FileExists(ExpandConstant('{app}\Tools\MonsterEditor\RynthCore.MonsterEditor.exe'))
; RynthCore.exe / Injector bundle .NET 10 (x86) — no Start Menu link nudging users to install a
; global runtime. See RynthCore-Prerequisites.txt for the optional x64 runtime (Loot/Monster tools).
Name: "{group}\Uninstall RynthCore"; Filename: "{uninstallexe}"

; Optional Desktop shortcut (created only when the desktopicon task is checked)
Name: "{autodesktop}\RynthCore"; Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Tasks: desktopicon

; ── Advisory: .NET 10+ Desktop Runtime (x64) for Suite tools (Loot/Monster) ──────────────
; RynthCore.exe and the injector are self-contained. Loot/Monster are x64 FDD and need a
; machine runtime; this page informs only — Next is never disabled (Rynth client policy; not UB-ILT).
; Registry rules below must match src/RynthCore.DesktopLog/NetDesktopX64Prerequisite.cs (C#) — change both together.
[Code]
const
  { Official landing page: user picks the x64 Desktop Runtime download. }
  DotNet10DesktopDownloadUrl = 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime';

var
  PrereqPage: TWizardPage;
  PrereqStatus: TNewStaticText;
  PrereqButtonDownload: TNewButton;
  PrereqButtonRecheck: TNewButton;

{ Path A: shared Windows Desktop pack (FDD for WinForms/WPF) — e.g. Microsoft.WindowsDesktop.App\10.0.0 }
function IsWindowsDesktopSharedFramework10Plus: Boolean;
var
  Names: TArrayOfString;
  I, Major, DotPos: Integer;
  S: String;
begin
  Result := False;
  if not RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\shared\Microsoft.WindowsDesktop.App', Names) then
    Exit;
  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    S := Names[I];
    DotPos := Pos('.', S);
    if DotPos > 0 then
    begin
      Major := StrToIntDef(Copy(S, 1, DotPos - 1), 0);
      if Major >= 10 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

{ Path B: .NET host registration (see learn.microsoft.com runtime discovery) — sharedhost Version 10+.
  Uses HKLM64\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost\Version
  and the parallel Microsoft\dotnet\Setup key when present. }
function IsSharedHostVersion10PlusX64: Boolean;
var
  V: String;
  DotPos: Integer;
  Major: Integer;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost', 'Version', V) then
  begin
    DotPos := Pos('.', V);
    if DotPos > 0 then
    begin
      Major := StrToIntDef(Copy(V, 1, DotPos - 1), 0);
      if Major >= 10 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\dotnet\Setup\InstalledVersions\x64\sharedhost', 'Version', V) then
  begin
    DotPos := Pos('.', V);
    if DotPos > 0 then
    begin
      Major := StrToIntDef(Copy(V, 1, DotPos - 1), 0);
      if Major >= 10 then
        Result := True;
    end;
  end;
end;

{ Path C: SDK / workload bands under Microsoft\dotnet\InstalledManifests\x64
  (e.g. ...Manifest-10.0.100). }
function IsDotNet10BandInInstalledManifestsX64: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  S, L: String;
begin
  Result := False;
  if not RegGetSubkeyNames(HKLM64, 'SOFTWARE\Microsoft\dotnet\InstalledManifests\x64', Names) then
    Exit;
  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    S := Names[I];
    L := LowerCase(S);
    if Pos('manifest-10.', L) > 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ Satisfied if desktop pack 10+, InstalledManifests 10.x band, or x64 sharedhost Version 10+ (Path B). }
function IsNetDesktop10PlusX64Present: Boolean;
begin
  Result := False;
  { 32-bit Windows: no x64 Desktop host; treat as "not applicable" and let setup proceed. }
  if (not IsWin64) then
  begin
    Result := True;
    Exit;
  end;
  if IsWindowsDesktopSharedFramework10Plus or IsDotNet10BandInInstalledManifestsX64 or IsSharedHostVersion10PlusX64 then
    Result := True;
end;

procedure PrereqUpdateStatus;
var
  Ok: Boolean;
begin
  Ok := IsNetDesktop10PlusX64Present;
  if Ok then
    PrereqStatus.Font.Color := $008000
  else
    PrereqStatus.Font.Color := $0000FF;
  if (not IsWin64) then
  begin
    PrereqStatus.Font.Color := $004040;
    PrereqStatus.Caption := '32-bit Windows: the bundled Loot/Monster tools need a 64-bit OS; ' +
      'RynthCore.exe will still be installed. You can skip the .NET x64 check.';
  end
  else if Ok then
    PrereqStatus.Caption := 'Microsoft .NET Desktop Runtime 10 or newer (x64) is already installed. You can continue.'
  else
    PrereqStatus.Caption := 'Not detected: Microsoft .NET 10+ Desktop Runtime (x64) for the Loot/Monster tools. ' +
      'You can still continue — RynthCore.exe does not need this. Install the x64 Desktop runtime when you use those tools ' +
      '(use "Open download page" / "Check again").' + #13#10#13#10 +
      'RynthCore ships with its own .NET for the launcher and injector.';
  PrereqButtonRecheck.Enabled := not Ok;
  { Always allow Next: advisory-only page for the Rynth bundle installer. }
  WizardForm.NextButton.Enabled := True;
end;

procedure PrereqOpenDownloadClick(Sender: TObject);
var
  E: Integer;
begin
  if not ShellExec('open', DotNet10DesktopDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, E) then
    MsgBox('Could not open the web browser. Copy this address:'#13#10#13#10 + DotNet10DesktopDownloadUrl, mbError, MB_OK);
end;

procedure PrereqRecheckClick(Sender: TObject);
begin
  PrereqUpdateStatus;
end;

procedure InitializeWizard;
var
  RowTop: Integer;
begin
  PrereqPage := CreateCustomPage(
    wpWelcome,
    'Optional runtime (Suite tools)',
    'The Loot Editor and Monster Editor are x64 apps and need the Microsoft .NET 10+ Desktop Runtime (x64) on the PC to run. ' +
    'Setup does not require it to finish — RynthCore.exe and the injector bundle their own .NET.'
  );
  PrereqStatus := TNewStaticText.Create(PrereqPage);
  with PrereqStatus do
  begin
    Parent := PrereqPage.Surface;
    Top := 0;
    Left := 0;
    WordWrap := True;
    AutoSize := False;
    Height := 128;
    Width := PrereqPage.SurfaceWidth;
  end;
  RowTop := PrereqStatus.Top + PrereqStatus.Height + 16;
  PrereqButtonDownload := TNewButton.Create(PrereqPage);
  with PrereqButtonDownload do
  begin
    Parent := PrereqPage.Surface;
    Top := RowTop;
    Left := 0;
    Width := 200;
    Caption := 'Open download page';
    OnClick := @PrereqOpenDownloadClick;
  end;
  PrereqButtonRecheck := TNewButton.Create(PrereqPage);
  with PrereqButtonRecheck do
  begin
    Parent := PrereqPage.Surface;
    Top := RowTop;
    Left := PrereqButtonDownload.Left + PrereqButtonDownload.Width + 12;
    Width := 160;
    Caption := 'Check again';
    OnClick := @PrereqRecheckClick;
  end;
  PrereqUpdateStatus;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (PrereqPage <> nil) and (CurPageID = PrereqPage.ID) then
    PrereqUpdateStatus;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { Silent (or very silent) install: no GUI to open the download page. Admin must pre-install the x64 runtime. }
  if (PrereqPage <> nil) and (PageID = PrereqPage.ID) and WizardSilent then
    Result := True
  else
    Result := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  { Prereq page is advisory only — never block Next on .NET detection (Rynth client installer only). }
  Result := True;
end;

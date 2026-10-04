; RynthCore + RynthSuite full installer
; Build with: Build-Installer.ps1 (requires Inno Setup 6) — it builds every component from source
; (launcher, engine, loader, RynthAi plugin, Loot Editor, Monster Editor) and stages them under
; installer\staging\core and installer\staging\suite before invoking ISCC on this file.
; Source: https://jrsoftware.org/isdl.php
;
; Install locations are user-defined:
;   * RynthCore folder  ({app})            — the standard "Select Destination Location" page
;   * RynthSuite folder ({code:GetSuiteDir}) — a second folder page right after it
; Both are recorded under HKA\Software\Rynth (CoreDir / SuiteDir); the engine, launcher and plugins
; resolve every path from there (RynthInstallPaths.cs), so neither folder has to be under C:\Games.
; Silent installs: /DIR="<core folder>" /SUITEDIR="<suite folder>".
;
; NOTE: No ISPP (#define / {#...}) macros are used in this file.
; Build-Installer.ps1 replaces the AppVersion placeholder via text substitution
; before invoking ISCC, so the preprocessor has nothing to expand.
; AppVersion placeholder — replaced by Build-Installer.ps1 before ISCC runs:
; APPVERSION_PLACEHOLDER=0.0.0

[Setup]
; Same AppId as every earlier RynthCore / RynthBundle installer, so this upgrades them in place.
AppId={{A8B4C2D1-E3F5-4678-9ABC-DEF012345678}
AppName=RynthCore
AppVerName=RynthCore + RynthSuite 0.0.0
AppVersion=0.0.0
AppPublisher=RynthCore
AppPublisherURL=https://aelrynth.com/rynth.html
UninstallDisplayName=RynthCore + RynthSuite
DefaultDirName=C:\Games\RynthCore
DisableDirPage=no
UsePreviousAppDir=yes
DefaultGroupName=RynthCore
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=RynthCore-Setup
SetupIconFile=..\src\RynthCore.App.Avalonia\LogoCore.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Run as lowest privilege; Windows will prompt for elevation only when
; the chosen install folder requires it (e.g. C:\Games\).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Close a running launcher / editors before replacing their files.
CloseApplications=yes
; 32-bit app; works on 32/64-bit Windows (HKA registry writes land in the 32-bit view,
; which is exactly where RynthInstallPaths reads HKLM from).
ArchitecturesAllowed=x86compatible
ArchitecturesInstallIn64BitMode=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full";   Description: "Full install (RynthCore + RynthSuite)"
Name: "core";   Description: "RynthCore only"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "core";          Description: "RynthCore launcher, engine and Loot Editor"; Types: full core custom; Flags: fixed
Name: "rynthai";       Description: "RynthSuite: RynthAi plugin (installed and registered with the launcher)"; Types: full
Name: "monstereditor"; Description: "RynthSuite: Monster Editor"; Types: full

[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

; Files left by older layouts (the 0.4.x RynthBundle put a framework-dependent launcher, the engine
; and plugins straight into {app}). Mixing those with a newer launcher is what broke 2026-10-04's
; reinstall (RynthCore.App.Avalonia.dll from 0.4.7 + a newer deps.json → missing DesktopLog).
; Only loose top-level binaries/metadata are removed; every file the new build needs is copied back
; by [Files] right after. User data (Logs, AcClient, NavData, DecalBridge, ...) is never touched.
[InstallDelete]
Type: files;          Name: "{app}\*.dll"
Type: files;          Name: "{app}\*.json"
Type: files;          Name: "{app}\*.pdb"
Type: files;          Name: "{app}\*.exp"
Type: files;          Name: "{app}\*.lib"
Type: files;          Name: "{app}\*.previous"
Type: files;          Name: "{app}\RynthCore.App.Avalonia.exe"
Type: files;          Name: "{app}\RynthCore-Prerequisites.txt"
Type: files;          Name: "{app}\Runtime\*.dll"
Type: files;          Name: "{app}\Runtime\*.json"
Type: files;          Name: "{app}\Runtime\*.pdb"
Type: files;          Name: "{app}\Runtime\RynthCore.Injector.exe"
Type: filesandordirs; Name: "{app}\Runtime\Plugins"

[Files]
; RynthCore: launcher, Runtime\ (engine, loader, native deps), Tools\LootEditor, release.txt
Source: "staging\core\*"; DestDir: "{app}"; Components: core; Flags: ignoreversion recursesubdirs createallsubdirs

; RynthSuite: the RynthAi plugin lives next to its data folders in the chosen Suite folder.
Source: "staging\suite\RynthAi\RynthCore.Plugin.RynthAi.dll"; DestDir: "{code:GetSuiteDir}\RynthAi"; Components: rynthai; Flags: ignoreversion
; Monster Editor: RynthAi's dashboard launches it from <RynthAi>\MonsterEditor\.
Source: "staging\suite\RynthAi\MonsterEditor\*"; DestDir: "{code:GetSuiteDir}\RynthAi\MonsterEditor"; Components: monstereditor; Flags: ignoreversion recursesubdirs createallsubdirs

; RynthAi data directories (created once; never removed on uninstall — they hold user profiles)
[Dirs]
Name: "{code:GetSuiteDir}\RynthAi";                             Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\NavProfiles";                 Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\LootProfiles";                Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\MetaFiles";                   Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\MetaProfiles";                Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\SettingsProfiles";            Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\SettingsProfiles\ACEmulator"; Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\MonsterProfiles";             Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\LuaScripts";                  Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\Logs";                        Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\pvars";                       Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\ItemGiver";                   Components: rynthai; Flags: uninsneveruninstall
Name: "{code:GetSuiteDir}\RynthAi\AutoVendor";                  Components: rynthai; Flags: uninsneveruninstall

; Install locations for the engine / launcher / plugins (see RynthInstallPaths.cs), plus a one-shot
; hand-off the launcher consumes on next start to add the plugin to its Plugins list.
; HKA = HKCU for a per-user install, HKLM for an all-users (elevated) install.
[Registry]
Root: HKA; Subkey: "Software\Rynth"; ValueType: string; ValueName: "CoreDir";  ValueData: "{app}";              Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Rynth"; ValueType: string; ValueName: "SuiteDir"; ValueData: "{code:GetSuiteDir}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Rynth"; ValueType: string; ValueName: "PendingPluginRegistration"; ValueData: "{code:GetSuiteDir}\RynthAi\RynthCore.Plugin.RynthAi.dll"; Components: rynthai; Flags: uninsdeletevalue

; Shortcuts
[Icons]
; Start Menu
Name: "{group}\RynthCore"; Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Comment: "Launch RynthCore and inject into Asheron's Call"
Name: "{group}\Loot Editor"; Filename: "{app}\Tools\LootEditor\RynthCore.LootEditor.exe"; WorkingDir: "{app}\Tools\LootEditor"; Comment: "Edit VTank-style loot profiles"
Name: "{group}\Monster Editor"; Filename: "{code:GetSuiteDir}\RynthAi\MonsterEditor\RynthCore.MonsterEditor.exe"; WorkingDir: "{code:GetSuiteDir}\RynthAi\MonsterEditor"; Components: monstereditor; Comment: "Edit RynthAi monster profiles"
Name: "{group}\RynthSuite Folder"; Filename: "{code:GetSuiteDir}"; Components: rynthai
Name: "{group}\Uninstall RynthCore"; Filename: "{uninstallexe}"

; Optional Desktop shortcut (created only when the desktopicon task is checked)
Name: "{autodesktop}\RynthCore"; Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Interactive install: offer to start the launcher on the last page.
Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Description: "Launch RynthCore"; Flags: nowait postinstall skipifsilent
; The launcher's updater runs this installer with /SILENT /RELAUNCH after closing itself;
; bring it back when the upgrade is done.
Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser; Check: CmdLineParamExists('/RELAUNCH')

[Code]
const
  DefaultSuiteDir = 'C:\Games\RynthSuite';

var
  SuiteDirPage: TInputDirWizardPage;
  { True once the user (or /SUITEDIR / a previous install) chose the Suite folder explicitly;
    until then it follows the RynthCore folder as a sibling "RynthSuite" folder. }
  SuiteDirPinned: Boolean;

function CmdLineParamExists(const Value: string): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

{ Returns the value of a /NAME=value command-line switch, or '' when absent. }
function CmdLineParamValue(const Name: string): string;
var
  I: Integer;
  Prefix: string;
begin
  Result := '';
  Prefix := '/' + Name + '=';
  for I := 1 to ParamCount do
    if CompareText(Copy(ParamStr(I), 1, Length(Prefix)), Prefix) = 0 then
    begin
      Result := RemoveQuotes(Copy(ParamStr(I), Length(Prefix) + 1, MaxInt));
      Exit;
    end;
end;

{ Suite folder recorded by an earlier install (HKCU first, then HKLM), or ''. }
function RegisteredSuiteDir: string;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, 'Software\Rynth', 'SuiteDir', Result) then
    if not RegQueryStringValue(HKLM, 'Software\Rynth', 'SuiteDir', Result) then
      Result := '';
end;

{ Default Suite folder for a given RynthCore folder: its sibling "RynthSuite". }
function SiblingSuiteDir(const CoreDir: string): string;
begin
  Result := AddBackslash(ExtractFileDir(RemoveBackslashUnlessRoot(CoreDir))) + 'RynthSuite';
end;

// {code:GetSuiteDir}: the chosen RynthSuite folder, without a trailing backslash.
function GetSuiteDir(Param: string): string;
begin
  if SuiteDirPage = nil then
    Result := DefaultSuiteDir
  else if WizardSilent and not SuiteDirPinned then
    { Silent installs never run NextButtonClick: follow /DIR= with a sibling RynthSuite folder. }
    Result := SiblingSuiteDir(WizardDirValue)
  else
    Result := RemoveBackslashUnlessRoot(Trim(SuiteDirPage.Values[0]));
end;

procedure InitializeWizard;
var
  Initial: string;
begin
  { After the Components page, so ShouldSkipPage knows whether any Suite component was picked. }
  SuiteDirPage := CreateInputDirPage(wpSelectComponents,
    'Select RynthSuite Location',
    'Where should the RynthSuite plugins and their data be installed?',
    'RynthAi and the Monster Editor will be installed into the RynthSuite folder below, and your ' +
    'profiles (nav, loot, meta, settings) are kept there. To continue, click Next. ' +
    'If you would like to select a different folder, click Browse.',
    False, 'RynthSuite');
  SuiteDirPage.Add('');

  { Priority: /SUITEDIR= → previous install of this setup → registry → C:\Games\RynthSuite. }
  Initial := CmdLineParamValue('SUITEDIR');
  if Initial = '' then Initial := GetPreviousData('SuiteDir', '');
  if Initial = '' then Initial := RegisteredSuiteDir;
  SuiteDirPinned := Initial <> '';
  if Initial = '' then Initial := DefaultSuiteDir;
  SuiteDirPage.Values[0] := Initial;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'SuiteDir', GetSuiteDir(''));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { No Suite components selected → no Suite folder to choose. }
  Result := (SuiteDirPage <> nil) and (PageID = SuiteDirPage.ID)
            and not WizardIsComponentSelected('rynthai')
            and not WizardIsComponentSelected('monstereditor');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir: string;
begin
  Result := True;

  { Leaving the RynthCore folder page: keep an un-pinned Suite folder beside the chosen Core folder. }
  if (CurPageID = wpSelectDir) and not SuiteDirPinned then
    SuiteDirPage.Values[0] := SiblingSuiteDir(WizardDirValue);

  if (SuiteDirPage <> nil) and (CurPageID = SuiteDirPage.ID) then
  begin
    Dir := GetSuiteDir('');
    { Require an absolute local or UNC path (e.g. D:\Games\RynthSuite or \\server\share\RynthSuite). }
    if (Length(Dir) < 3) or not ((Copy(Dir, 2, 2) = ':\') or (Copy(Dir, 1, 2) = '\\')) then
    begin
      MsgBox('Please choose a full folder path for RynthSuite, for example D:\Games\RynthSuite.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    SuiteDirPinned := True;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine;
  if WizardIsComponentSelected('rynthai') or WizardIsComponentSelected('monstereditor') then
    Result := Result + 'RynthSuite location:' + NewLine + Space + GetSuiteDir('') + NewLine;
  Result := Result + NewLine + MemoComponentsInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + MemoTasksInfo;
end;

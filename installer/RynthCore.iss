; RynthCore + RynthAi Installer
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
AppPublisherURL=https://aelrynth.com/git/rynth/RynthCore
DefaultDirName=C:\Games\RynthCore
DisableDirPage=no
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
; 32-bit app; works on 32/64-bit Windows
ArchitecturesAllowed=x86 x64 arm64
ArchitecturesInstallIn64BitMode=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

; Program files
[Files]
; Launcher + engine runtime + Loot Editor go to {app} (default C:\Games\RynthCore\)
Source: "staging\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Plugin DLL goes to its canonical home next to plugin data dirs.
; Engine does NOT auto-scan this folder — user adds the full DLL path in the
; launcher's Plugins tab to enable the plugin. See BUILD.md "Deploy RynthAi Plugin".
Source: "staging\plugins\RynthAi\RynthCore.Plugin.RynthAi.dll"; DestDir: "C:\Games\RynthSuite\RynthAi"; Flags: ignoreversion

; Data directories (created once; never removed on uninstall)
[Dirs]
Name: "C:\Games\RynthSuite\RynthAi";                             Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\NavProfiles";                 Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\LootProfiles";                Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\MetaFiles";                   Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\MetaProfiles";                Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\SettingsProfiles";            Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\SettingsProfiles\ACEmulator"; Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\LuaScripts";                  Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\Logs";                        Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\pvars";                       Flags: uninsneveruninstall
Name: "C:\Games\RynthSuite\RynthAi\ItemGiver";                   Flags: uninsneveruninstall

; Shortcuts
[Icons]
; Start Menu
Name: "{group}\RynthCore"; Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Comment: "Launch RynthCore and inject into Asheron's Call"
Name: "{group}\Loot Editor"; Filename: "{app}\Tools\LootEditor\RynthCore.LootEditor.exe"; WorkingDir: "{app}\Tools\LootEditor"; Comment: "Edit VTank-style loot profiles"
Name: "{group}\Uninstall RynthCore"; Filename: "{uninstallexe}"

; Optional Desktop shortcut (created only when the desktopicon task is checked)
Name: "{autodesktop}\RynthCore"; Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Interactive install: offer to start the launcher on the last page.
Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Description: "Launch RynthCore"; Flags: nowait postinstall skipifsilent
; The launcher's updater runs this installer with /SILENT /RELAUNCH after closing itself;
; bring it back when the upgrade is done.
Filename: "{app}\RynthCore.exe"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser; Check: CmdLineParamExists('/RELAUNCH')

[UninstallRun]
; Decal + RynthCore (experimental): remove the Decal bridge's registration, only
; what the launcher added (see docs/DECAL_BRIDGE_PLAN.md). Every release ships the bridge
; ({app}\DecalBridge, staged by Build-Installer.ps1), so this always runs; with nothing
; registered it changes nothing. Runs before the files are removed. It removes both entries
; the installer writes (see RegisterDecalBridge in [Code]): the per-user one, and the
; machine-wide one - directly when the uninstaller is elevated, otherwise after Windows'
; administrator prompt, which appears only when a machine-wide entry exists.
Filename: "{app}\RynthCore.exe"; Parameters: "--decal-bridge-unregister"; Flags: runhidden waituntilterminated; RunOnceId: "RynthCoreDecalBridgeUnregister"

[Code]
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

(* -------------------------------------------------------------------------------------
  Decal + RynthCore (experimental): register the RynthCore Decal bridge on every install
  AND every update (the launcher's updater runs this installer with /SILENT), in BOTH
  places a Decal client can read its filter list from:

    1. Machine-wide: HKLM\SOFTWARE\WOW6432Node\Decal\NetworkFilters\{5B3E0D57-...}
       (32-bit view). An elevated AC (RynthCore or AC run as administrator), or any AC when
       UAC or its registry virtualization is off, reads ONLY this key - the per-user entry
       is invisible to it (a player's Decal + RynthCore client never loaded the bridge,
       2026-10-03). A non-elevated AC without a per-user copy reads it too.
    2. Per-user: this user's VirtualStore copy of that key, ONLY if the copy already exists.
       A non-elevated AC is virtualized and, once a copy exists, reads nothing but the copy.
       A missing copy is never created: it would hide every HKLM filter added later from
       this user's non-elevated clients.

  Both entries are harmless while no account uses Decal + RynthCore: every Decal client
  loads the bridge, which stays idle (no hooks, no threads) unless the RynthCore loader is
  in the same client. Uninstall removes both ([UninstallRun] --decal-bridge-unregister).

  Rights. This installer runs with the lowest privileges (PrivilegesRequired=lowest; the
  updater starts it without elevation). Writing HKLM needs administrator rights, so:
    - Setup already elevated (installed "for all users", or started by an elevated
      launcher): written directly, no prompt.
    - Not elevated: Windows' administrator prompt is shown ONCE - only when Decal is
      installed, the machine-wide entry is missing or points at another folder, and this
      user has a Decal + RynthCore account (decal-accounts.json). Saying no is harmless:
      the launcher checks before every Decal + RynthCore launch and repairs it (writing it
      directly when it runs elevated, the case that needs it).
  The commands are RynthCore.exe's own (Program.cs): --decal-bridge-register-machine and
  --decal-bridge-install-user, so the installer and the launcher write identical entries.
  ------------------------------------------------------------------------------------- *)
const
  BridgeEntryKey = 'SOFTWARE\Decal\NetworkFilters\{5B3E0D57-2C41-4F8A-9D6E-8C1B70DECA11}';
  BridgeObject = 'RynthCore.DecalBridge.BridgeFilter';

function DecalInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM32, 'SOFTWARE\Decal\Agent');
end;

function BridgeDir: String;
begin
  Result := ExpandConstant('{app}\DecalBridge');
end;

function MachineBridgeEntryCurrent: Boolean;
var
  P, O: String;
  E: Cardinal;
begin
  Result := RegQueryStringValue(HKLM32, BridgeEntryKey, 'Path', P) and
            (CompareText(RemoveBackslashUnlessRoot(P), BridgeDir) = 0) and
            RegQueryStringValue(HKLM32, BridgeEntryKey, 'Object', O) and (O = BridgeObject) and
            RegQueryDWordValue(HKLM32, BridgeEntryKey, 'Enabled', E) and (E = 1);
end;

function UsesDecalBridge: Boolean;
begin
  Result := FileExists(ExpandConstant('{userappdata}\RynthCore\decal-accounts.json'));
end;

procedure RegisterDecalBridge;
var
  Exe, Params: String;
  Code: Integer;
begin
  if not DecalInstalled then Exit;
  if not FileExists(BridgeDir + '\RynthCore.DecalBridge.dll') then Exit;
  Exe := ExpandConstant('{app}\RynthCore.exe');
  Params := '--decal-bridge-register-machine "' + BridgeDir + '"';
  if not MachineBridgeEntryCurrent then
  begin
    if IsAdmin then
    begin
      if not Exec(Exe, Params, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) then
        Log('Decal bridge: machine-wide registration did not start: ' + SysErrorMessage(Code))
      else
        Log('Decal bridge: machine-wide registration exit code ' + IntToStr(Code));
    end
    else if UsesDecalBridge then
    begin
      if not ShellExec('runas', Exe, Params, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) then
        Log('Decal bridge: elevated machine-wide registration not done (declined or failed): ' + SysErrorMessage(Code))
      else
        Log('Decal bridge: elevated machine-wide registration exit code ' + IntToStr(Code));
    end
    else
      Log('Decal bridge: machine-wide entry missing; not elevated and no Decal + RynthCore account - left to the launcher.');
  end;
  { Per-user half, as the user who started Setup (the same user unless Setup was elevated
    with other credentials). }
  if not ExecAsOriginalUser(Exe, '--decal-bridge-install-user "' + BridgeDir + '"', ExpandConstant('{app}'),
                            SW_HIDE, ewWaitUntilTerminated, Code) then
    Log('Decal bridge: per-user step did not start: ' + SysErrorMessage(Code));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { After the files, before [Run] (which relaunches the launcher after an update). }
  if CurStep = ssPostInstall then
    RegisterDecalBridge;
end;

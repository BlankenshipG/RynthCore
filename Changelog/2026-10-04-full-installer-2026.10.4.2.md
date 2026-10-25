# RynthCore 2026.10.4.2: full RynthCore + RynthSuite installer

**Release date:** 2026-10-04
**Installer:** `installer\Output\RynthCore-Setup-2026.10.4.2.exe` (also copied to `RynthCore-Setup.exe`)
**Previous release:** `2026.10.4.1` (archived to `installer\previous-release\RynthCore-Setup-2026.10.4.1.exe`)
**Branch:** `feat/full-installer` (local test build, not on `main`), based on `fix/tmds-dbus-advisory`
**Bundled RynthSuite:** RynthAi `0.5.3`, Loot Editor, Monster Editor (from RynthSuite `feat/ilt-hub`)

## Why: the launcher would not start

On 2026-10-04 an old **RynthBundle 0.4.7** installer was run over the 2026.10.3.4 install. It replaced
`RynthCore.App.Avalonia.dll` with the 0.4.x build, which needs `RynthCore.DesktopLog.dll`, but left the newer
`RynthCore.App.Avalonia.deps.json`, which does not list it. The renamed apphost (`RynthCore.exe`) loads exactly
what the deps.json lists, so .NET failed at startup with `FileNotFoundException: RynthCore.DesktopLog`
(Event Log, .NET Runtime 1026). The new installer fixes such an install in place (see **Cleanup** below).

## One installer, two folders you choose

- **Wizard pages:**
  - **Select Destination Location** picks the RynthCore folder (default `C:\Games\RynthCore`).
  - **Select Components** offers Full, RynthCore only, or Custom. RynthCore is always installed; RynthAi and the
    Monster Editor are optional.
  - **Select RynthSuite Location** picks the RynthSuite folder. It defaults to a `RynthSuite` folder next to
    the RynthCore folder you picked, or the folder used last time. The page is skipped when no Suite component is selected.
- **Silent installs:** `RynthCore-Setup-2026.10.4.2.exe /SILENT /DIR="D:\Games\RynthCore" /SUITEDIR="D:\Games\RynthSuite"`.
  Without `/SUITEDIR` the Suite goes next to `/DIR`, or to the previously used folder.
- **Registry:** both folders are written to `Software\Rynth` (`CoreDir`, `SuiteDir`). That is HKCU for per-user installs
  and HKLM for all-users installs. All of RynthCore and RynthSuite now read them.
- **Plugin auto-registration:** the installer leaves `PendingPluginRegistration` with the RynthAi DLL path. On the next
  start the launcher adds it to the Plugins list (replacing any stale entry with the same file name whose file is gone),
  syncs `engine.json` and clears the value. A fresh install therefore loads RynthAi with no manual setup.
- **Shortcuts:** RynthCore, Loot Editor, Monster Editor, RynthSuite folder, Uninstall, and an optional desktop icon.

### Cleanup of older layouts
Before copying, the installer deletes leftovers from 0.4.x and RynthBundle installs in the RynthCore folder:
root `*.dll`/`*.json`/`*.pdb`, `RynthCore.App.Avalonia.exe`, old `Runtime\*.dll`/`*.json`, `Runtime\RynthCore.Injector.exe`
and `Runtime\Plugins\`. Then it lays down a complete, matching set. User data (`Logs`, settings in `%APPDATA%\RynthCore`,
everything under `<SuiteDir>\RynthAi`) is never touched, and the RynthSuite data folders survive an uninstall.

## Code: no more hard-coded `C:\Games\...`

New `src/RynthCore.App/RynthInstallPaths.cs`, linked into the launcher, engine, loader, injector and status agent.
It resolves the folders in this order: `RYNTHCORE_DIR`/`RYNTHSUITE_DIR` environment variables, then HKCU, then HKLM
(32-bit view), then the old `C:\Games\...` defaults. It uses a direct `RegGetValueW` P/Invoke, so it is NativeAOT-safe.
The engine resolves it on its init worker, never under the loader lock.

| Was | Now |
|---|---|
| `C:\Games\RynthCore\Logs` (launcher, loader, injector, engine) | `<CoreDir>\Logs` |
| `C:\Games\RynthCore\AcClient\acclient.exe` (injector) | `<CoreDir>\AcClient\acclient.exe` |
| `C:\Games\RynthSuite\RynthAi\imgui.ini`, MetaFiles | `<SuiteDir>\RynthAi\...` |
| Status agent defaults | derived from the resolved folders |

## Build script (`installer\Build-Installer.ps1`)
- **Publishes:** launcher, Engine, Loader, the RynthAi plugin, the Loot Editor, and now the **Monster Editor**
  (self-contained win-x86).
- **Staging:** RynthCore goes to `staging\core`, RynthSuite to `staging\suite\RynthAi` (plugin plus `MonsterEditor\`).
- **Versions:** the RynthAi plugin is no longer stamped with the installer version. It keeps its own semantic version
  (0.5.3), and everything else gets `-p:Version=<installer version>`.
- **New check:** every project assembly listed in the launcher's `deps.json` must be staged next to it. This guards
  directly against the DesktopLog failure above.
- **Previous release:** `installer\previous-release\last-built-version.txt` tracks the last build. When the version
  changes, that build's installer is archived to `previous-release\` (git-ignored) before the new one is built.
- **Output:** `RynthCore-Setup-<version>.exe`, plus a `RynthCore-Setup.exe` copy.

## Not included
RynthChat, RynthJuice, RynthNav, RynthTracker and RynthVision are experimental and stay out of the installer.
RynthNav and RynthJuice still hard-code `C:\Games\RynthCore`.

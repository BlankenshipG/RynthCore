# 2026.10.5.60 - launcher: undo a saved UserPreferences.ini that stops the client starting

Previous release: 2026.10.5.59. Launcher project version 0.1.7 (was 0.1.6).

## Problem

A client launched from RynthCore showed "Could not initialize Direct3D. Please ensure that
DirectX 9.0 or higher is installed." twice, then "The game encountered a fatal DirectX issue
while attempting to start. Try a different screen resolution or bit depth...", and exited
about 12 seconds after start. The same client started fine from another launcher.

The engine was not involved: its D3D9 hooks only start at login, and the log had no D3D9
lines. The difference was the launcher. Before every launch it copies the account's saved
game settings (`%APPDATA%\RynthCore\prefs\<account>@<server>.ini`, or the file set on the
profile) over the live `Documents\Asheron's Call\UserPreferences.ini`. The game reads its
resolution and full-screen mode from that file. A saved copy with a display mode the PC can't
use (another monitor, an old resolution) fails every launch, and the saved copy is only
refreshed after a normal session, so it was restored again on every launch. Another launcher
uses the live file, which is why it worked.

## Change

- When a client quits within 30 s (the existing "counted as crash" window) and its launch
  swapped in a saved UserPreferences.ini that differs from the live file:
  - The saved copy is renamed to `<name>.ini.failed-yyyyMMdd-HHmmss` (kept, never swapped in
    again). The account's old name-only copy (`<account>.ini`) is set aside too when it holds
    the same settings, because it re-seeds a missing per-server copy.
  - The live UserPreferences.ini is put back to what it was before the swap, unless another
    launch has swapped it since.
  - The activity log says what happened. The next launch uses the live file, and the next
    normal session saves a fresh copy.
- A file the user picked on the profile (UserPrefsPath) is never renamed; the live file is
  still put back and the activity log points at the profile setting.
- Nothing changes when the saved copy matched the live file (the swap can't have caused the
  exit), e.g. a missing password or a server that is down.

## Code

- `src/RynthCore.App/UserPrefsSwapRecovery.cs` (new): `UserPrefsSwap` record (stash path,
  live path, stash bytes, live bytes before the swap), `SwapChangedPrefs`, `Recover`
  (quarantine + restore), `QuarantinePath`.
- `src/RynthCore.App/AcClientLaunchSettingsService.cs`: `SwapUserPreferences` returns the
  `UserPrefsSwap` (was `bool`) and reads the live file before overwriting it.
- `src/RynthCore.App.Avalonia/MainWindow.axaml.cs`: the three launch paths (RynthCore, Decal /
  Decal + RynthCore, no injection) use `ApplyPrefsSwap` and record the swap per PID
  (`TrackPrefsSwap`); `DetectQuickExits` calls `RecoverFailedPrefsSwap` for a quick exit. A swap
  generation counter decides whether the live file can still be put back.
  `ResolveLegacyUserPrefsPath` is shared with `ResolveUserPrefsPath`.
- `RynthCore.App.Avalonia.csproj`: links the new file; version 0.1.7.

## Tests

- `tools/LauncherSettingsTests`: 5 new scenarios (swap changed the live file or not; failed
  stash set aside and live file restored; a different name-only stash kept; profile override
  and later swap leave files alone; no live file before). `PASS: 163 checks`.
- `dotnet build src\RynthCore.App.Avalonia\RynthCore.App.Avalonia.csproj -c Release`: 0 errors.

## For an affected PC now

Rename or delete `%APPDATA%\RynthCore\prefs\<account>@<server>.ini` (and `<account>.ini`) for
the failing account, or clear the UserPreferences file on its profile, then launch again.

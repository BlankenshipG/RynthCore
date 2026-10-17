# 2026.10.18.2 — Launcher: per-account prefs swap uses the UserPreferences.ini AC actually reads

**Kind:** Hot push (Launcher). Previous `2026.10.18.1`.

## Problem

The launcher's per-account UserPreferences.ini swap, the multi-client flag (`ComputeUniquePort`),
the end-of-session snapshot, and the account dialog's Snapshot button all used
`Documents\Asheron's Call\UserPreferences.ini`. On this install AC reads and writes the copy in
the game folder: `C:\Turbine\Asheron's Call-ryn\UserPreferences.ini` changed every session, while
the Documents copy hadn't changed since April. So the per-account settings were swapped into a
file AC never read, and the snapshot saved a stale copy.

## Fix

- `AcClientLaunchSettingsService.LivePrefsPath(acclient path)`: the game folder's
  `UserPreferences.ini` when it exists, else the Documents one.
- Used by:
  - the swap, whose recovery undoes the same file it swapped;
  - the end-of-session snapshot;
  - the account dialog's Snapshot button.
- `ComputeUniquePort` is set in every copy that exists.
- Settings > Graphics (2026.10.18.1) already writes both copies; its tests now also check the
  game folder's file comes first (AcGraphicsPrefsTests 55/55).

## Files

- `src/RynthCore.App/AcClientLaunchSettingsService.cs`
- `src/RynthCore.App.Avalonia/MainWindow.axaml.cs`
- `src/RynthCore.App.Avalonia/AccountProfileDialog.axaml.cs`
- `tools/AcGraphicsPrefsTests/Program.cs`

## Checks

Launcher builds clean; LauncherSettingsTests 163/163; AcGraphicsPrefsTests 55/55.

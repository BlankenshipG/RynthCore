# 2026.10.18.3 — Graphics profiles per account; the window never fights AC's resolution

**Kind:** Hot push (Engine + Launcher). Previous `2026.10.18.2`.

## Problem

After the resolution was changed in AC's own settings, clients fought RynthAi's Graphics settings:
windows came back the wrong size, and clients sat in portal space. Every client shared the one
Settings > Graphics set (`graphics.json`).

## Cause

- The window was sized for the Resolution saved in RynthAi's Graphics settings, not for the
  resolution AC was actually drawing at. After a change in AC's own options, the two disagreed and
  RynthCore pushed the window back at every login.
- The size also came out 10 px too big each way (1770x1000 for 1760x990). It was worked out for a
  resizable frame, but AC's window has a fixed one.
- Every client edited one shared set, and every client wrote it into the shared
  `UserPreferences.ini` on each change. So one client's values landed on all the others.

## Fix

- **The window follows what AC really draws.** The engine samples AC's back-buffer size about once a
  second and logs any change. Window sizing uses it, falling back to the resolution AC launched
  with. This covers restoring at login, Reset Window Borders, turning borderless off, and
  borderless at resolution size. The frame is taken from the window's real style, so a framed
  window's game area is exactly AC's resolution.
- **A profile per account:**
  - `graphics.json` holds a profile per `server|account`. Settings > Graphics edits only this
    client's account, and each save re-reads the file so other accounts' entries are kept.
  - The launcher writes the launching account's profile into `UserPreferences.ini` just before
    it starts that account, under the prefs lock whenever any profile exists.
  - The per-character rows are gone; the account profile replaces them.
- **Clients no longer write the shared INI on every change.** Only the launcher and the "Write to
  AC's settings now" button do.
- Live changes are limited to the window mode, which is RynthCore's own window frame. Resolution and
  every render setting apply at the next launch.
- The 2026.10.18.1 shared `graphics.json` is ignored. On this PC it was backed up to
  `graphics.json.shared-2026.10.18.1.bak` and replaced with an empty per-account file.

## Files

- `src/RynthCore.Engine/Graphics/AcGraphicsPrefs.cs`: `AcGraphicsProfiles` and `AccountKey`.
- `src/RynthCore.Engine/Graphics/AcGraphics.cs`: account profile, draw size, no INI writes on edit.
- `src/RynthCore.Engine/ImGui/DX9Backend.cs`: `TryGetBackBufferSize`.
- `src/RynthCore.Engine/D3D9/EndSceneHook.cs`: samples the back buffer.
- `src/RynthCore.Engine/UI/AcWindowChrome.cs`: draw-size sizing; the real frame style.
- `src/RynthCore.Engine/UI/Data/SettingsData.cs`: per-account Graphics tab.
- `src/RynthCore.Engine/Compatibility/CharacterCaptureHooks.cs`: comment only.
- `src/RynthCore.App.Avalonia/MainWindow.axaml.cs`: launcher applies the account's profile.
- `tools/AcGraphicsPrefsTests`: 53 checks, per-account.

## Checks

Engine and launcher build clean; AcGraphicsPrefsTests 53/53.

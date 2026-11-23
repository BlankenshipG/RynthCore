# 2026.10.18.4 — test build full install

**Kind:** Test build installer. Previous installer `2026.10.18.2`.

Built from `test/hotbuild-2026-10-16-on-aelrynth` with `aelrynth/main` merged in (nothing newer
upstream).

## New since 2026.10.18.2

- **Graphics profiles per account (2026.10.18.3):**
  - Settings > Graphics edits only this client's account.
  - The launcher writes the launching account's profile into AC's `UserPreferences.ini` just before
    it starts the account, one account at a time.
  - The old shared `graphics.json` is ignored.
- **The window never fights AC's resolution:**
  - It is sized for what AC really draws at: the back buffer, else the launch resolution.
  - The frame comes from the window's real style, so the game area is exactly AC's resolution, not
    10 px bigger.
  - Clients no longer write AC's shared INI on every change.

## Checks

- Engine and launcher build clean; AcGraphicsPrefsTests 53/53.
- The staged engine has the back-buffer reader and the per-account profiles; the staged launcher
  has the per-account profiles and the live-prefs path.

## Install

Close every AC client and the launcher first (the launcher, loader and engine are replaced).

- `installer\Output\RynthCore-Setup-2026.10.18.4.exe`
- `installer\Output\RynthCore-2026.10.18.4-deploy.zip`

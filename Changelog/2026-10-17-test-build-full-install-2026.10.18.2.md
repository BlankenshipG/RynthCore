# 2026.10.18.2 — test build full install

**Kind:** Test build installer (major feature). Previous installer `2026.10.17.6`.

Built from `test/hotbuild-2026-10-16-on-aelrynth`. `aelrynth/main` (rynth's 2026.10.17.1) is
merged in; nothing newer upstream.

## New since 2026.10.17.6

- **Settings > Graphics (2026.10.18.1):**
  - AC's graphics options inside RynthAi's settings: window mode, resolution, refresh rate,
    vsync, and every option on AC's Graphics page.
  - A per-character resolution and window mode.
  - The launcher writes them into AC's `UserPreferences.ini` before every start, so they hold
    whatever AC last saved.
  - The window's size always follows the resolution; only its position is remembered.
- **Launcher (2026.10.18.2):** the per-account prefs swap, the multi-client flag, and the
  snapshots use the `UserPreferences.ini` in the game folder, which is the one AC reads on this
  install.
- **Window fixes (2026.10.17.7 and .8):**
  - The window position saves safely with many clients.
  - Restored windows are kept on-screen, so AC's toolbar can't be pushed off.
  - AC's 800x600 login-screen size is never saved or restored, so clients no longer start small.

## Checks

- Engine and launcher build clean.
- AcGraphicsPrefsTests 55/55; LauncherSettingsTests 163/163.
- The staged engine contains the Graphics tab; the staged launcher contains the graphics apply
  and the live-prefs path.

## Install

Close every AC client and the launcher first (the launcher, loader and engine are replaced).

- `installer\Output\RynthCore-Setup-2026.10.18.2.exe`
- `installer\Output\RynthCore-2026.10.18.2-deploy.zip`

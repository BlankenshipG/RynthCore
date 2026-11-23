# 2026.10.17.5 — Reset Window Borders; borderless position saved

**Kind:** Hot push (Engine). Previous `2026.10.17.4`.

## Asked for

A way to reset the window borders, and to save the window position in borderless mode.

## Added

Settings > Display:

- **Reset Window Borders** (button): turns borderless off and puts the frame back as a normal,
  resizable window, centred at three quarters of the monitor. It also replaces the saved window
  position, so the next launch isn't stuck either. Same as `/rc window reset`.
- **Borderless Fills Monitor** (new `engine.json` key `BorderlessFillMonitor`, default on):
  - On: borderless fills the whole monitor the game is on (as in 2026.10.17.4).
  - Off: borderless keeps the game exactly where, and at the size, the framed window had it. To
    set a custom borderless spot, turn Borderless Windowed off, move and size the window, then
    turn it back on.
  - Same as `/rc window fill on|off`.
- **Save Window Position** (button): saves where the window is now, framed or borderless, for the
  next launch. Same as `/rc window save`.

## Changed

- `%LOCALAPPDATA%\RynthCore\ac_window.txt` keeps the borderless rect (`bl_left`, `bl_top`,
  `bl_width`, `bl_height`) next to the framed rect (`left`, `top`, `width`, `height`). Both are
  saved every 10 seconds while Remember Open Windows and Positions is on, each only in its own mode.
- At launch with borderless on, the saved borderless rect comes back. With Fills Monitor on, that
  means the monitor it was on (multi-monitor). The saved framed rect is what you get when
  borderless is turned off.
- The "keep it there" check after AC re-sizes the window (DXVK device reset) now restores the
  chosen borderless rect, not only the full monitor. It still gives up after 4 tries a minute.

## Note

The placement file is shared by every client on this PC, as before, so all clients come back to
the same spot.

## Files

- `src/RynthCore.Engine/UI/AcWindowChrome.cs`
- `src/RynthCore.Engine/Plugins/EngineSettings.cs`
- `src/RynthCore.Engine/UI/Data/SettingsData.cs`
- `src/RynthCore.Engine/Compatibility/RynthCoreChatCommands.cs`

## Checks

Engine build clean (no warnings in changed files); EngineSettingsTests 226/226, including the
OwnedKeys check for the new key.

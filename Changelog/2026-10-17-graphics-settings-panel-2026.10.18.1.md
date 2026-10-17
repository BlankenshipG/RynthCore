# 2026.10.18.1 — Settings > Graphics: AC's graphics options in RynthAi, enforced every launch

**Kind:** Major feature (Engine + Launcher). Previous `2026.10.17.8`.

## Asked for

- Resetting the window size didn't respect AC's resolution setting.
- A graphics settings panel inside RynthAi's settings that covers everything on AC's own
  graphics pages, and that RynthAi enforces whether the client is windowed, full screen,
  framed or borderless.

## New: Settings > Graphics (after Display)

- **Window**
  - **Window mode:** Windowed, Borderless (fill the monitor), Borderless (resolution size), or
    Full screen (exclusive).
  - **Resolution:** the monitor's modes.
  - **Refresh rate:** Auto or a number of Hz.
  - **Sync to display refresh.**
- **This character:** its own window mode and resolution. These win over the global ones, and the
  launcher uses them when it starts that character.
- **Graphics:** every Render option from AC's own Graphics page:
  - landscape and scenery draw distance;
  - landscape and environment texture detail;
  - texture filtering;
  - landscape and building detail textures;
  - multi-pass alpha;
  - field of view;
  - screen brightness;
  - aspect ratio;
  - adaptive degrade, degrade bias and degrade distance.
- Every setting starts on **AC's choice**, which means RynthAi doesn't touch it.
- **Write to AC's settings now** and **Reset all to AC's choice** buttons.
- The value lists come from `acclient.exe` itself: VeryLow…Extreme; Bilinear, Trilinear, Sharp
  and Anisotropic; Normal and Wide.

## How it is enforced

- The settings are kept in `%APPDATA%\RynthCore\graphics.json`, shared by every client on the PC.
  Clients pick up each other's edits within a few seconds.
- AC reads `UserPreferences.ini` only at startup and rewrites it itself. So the launcher writes
  RynthAi's values into it just before every client starts, after any per-account prefs swap, so
  RynthAi's values win.
- The engine also writes it when a setting changes.
- Both write the game folder's `UserPreferences.ini` (which this install's clients actually use)
  and `Documents\Asheron's Call\UserPreferences.ini` when it exists.
- When per-character resolutions or window modes exist, launches take the prefs lock, so two
  clients starting together each read their own values.

## Window sizing follows the resolution

- The window's size always comes from AC's resolution: Settings > Graphics, else what AC started
  with. Only the window's position is remembered. This covers:
  - restoring the window at launch;
  - Reset Window Borders and `/rc window reset` (centred, the resolution's size plus the frame);
  - turning borderless off;
  - Borderless (resolution size).
- This also ends the "client starts small" class of bugs: a size saved at another resolution, or
  at the login screen, can no longer shrink the window.
- Changing the window mode or resolution in the panel changes the window at once. AC draws at a
  new resolution, and starts exclusive full screen, from the next launch.
- The Display toggles (Borderless Windowed, Borderless Fills Monitor, `/rc borderless`,
  `/rc window fill`) still work. Using them hands the global window mode back to them.

## Files

- `src/RynthCore.Engine/Graphics/AcGraphicsPrefs.cs` (new): the model, `graphics.json`, and INI
  editing. Shared with the launcher.
- `src/RynthCore.Engine/Graphics/AcGraphics.cs` (new): the engine side. Effective values, edits,
  coalesced background writes, display mode lists.
- `src/RynthCore.Engine/UI/AcWindowChrome.cs`: resolution-based sizing; `ApplyGraphicsChanged`.
- `src/RynthCore.Engine/UI/Data/SettingsData.cs`: the Graphics tab; the Display toggles hand the
  mode back.
- `src/RynthCore.Engine/Compatibility/RynthCoreChatCommands.cs`: `/rc borderless` and
  `/rc window fill` hand the mode back.
- `src/RynthCore.Engine/Compatibility/CharacterCaptureHooks.cs`: `ReadLaunchContext` is internal.
- `src/RynthCore.App.Avalonia/MainWindow.axaml.cs` and `RynthCore.App.Avalonia.csproj`: apply
  graphics before each launch, and the lock.
- `tools/AcGraphicsPrefsTests` (new): 52 checks.

## Checks

- Engine and launcher build clean.
- AcGraphicsPrefsTests 52/52: overrides, the FullScreen mapping, the JSON round-trip, in-place
  INI edits, and no write when nothing changed.

## Note

The launcher half ships with the next installer. Until then the engine writes the INI when a
setting changes, but AC can overwrite it when the client exits.

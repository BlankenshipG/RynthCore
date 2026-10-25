# 2026.10.5.65 - Settings > Display: borderless windowed and remember layout

Previous release: 2026.10.5.64.

## Change

Under **Settings > Display**:

- **Borderless Windowed** — strips the Asheron's Call title bar and borders while
  staying windowed (exclusive fullscreen is left alone). Stored in `engine.json`
  as `BorderlessWindowed` (default off); applies to every client on this PC.
- **Remember Open Windows and Positions** — when on (default), reopen Rynth panels
  where you left them, keep Vision HUD / `imgui.ini` layout, and restore the AC
  window position across launches. When off, do not overwrite `panel_state.txt` or
  reopen panels / AC placement from the last session (`RememberWindowLayout` in
  `engine.json`).
- **Save Layout Now** — writes open panels, AC window placement, Vision self-plate,
  and ImGui window positions to disk immediately.

## Files

| Area | Notes |
|---|---|
| `UI/AcWindowChrome.cs` | New: GWL_STYLE borderless + `ac_window.txt` placement |
| `Plugins/EngineSettings.cs` | `BorderlessWindowed`, `RememberWindowLayout` |
| `UI/Data/SettingsData.cs` | Display rows; `SettingKind.Color` (was missing on this branch) |
| `UI/PanelStateStore.cs` | Gate saves; `Flush()` for Save Now |
| `ImGui/ImGuiPanelHost.cs` / `UI/AvaloniaOverlay.cs` | Skip restore when remember is off |
| `ImGui/EngineFrameController.cs` | Throttled `AcWindowChrome.Tick` |

Also restored a few files that were already broken on `build-comp-10-11` so the
engine could compile again (`SettingsFace`, `ImGuiBar` DrawControls/BarMenu,
`RynthAiFace`, `MiniRemoteFace` + `.Sections`) from aelrynth `main`.

## Test plan

- [ ] Windowed AC: toggle Borderless Windowed on/off; chrome hides and returns.
- [ ] Exclusive fullscreen: toggle does not break the client.
- [ ] Arrange panels, Save Layout Now, restart client with Remember on: panels reopen.
- [ ] Remember off: rearrange panels, restart: previous saved layout is not overwritten;
      panels do not force-reopen from disk.

# 2026-10-04 — ILT Hub panel button, ImGui windows on top, live logging levels

Launcher 0.1.5. Needs RynthAi 0.6.18 for the RynthAi logging rows.

## ILT Hub button (Avalonia RynthAi panel)

* New **ILT Hub** launcher button (third row of the RynthAi panel launcher grid).
  Sends `hub show` through the plugin's `RynthPluginApplyRemoteCommand` export —
  the same path as typing `/ra hub show`.
* New `SendRemoteCmd(action, value)` helper marshals both strings, calls the
  export and frees them (the plugin copies them and applies on its pump thread).

## Plugin ImGui windows draw on top of the Avalonia layer

* `EngineFrameController.RunImGuiFrame` still builds the ImGui frame at the same
  point, but no longer submits the draw data. `EndSceneHook` now calls the new
  `EngineFrameController.RenderDeferredImGui()` **after**
  `OverlayTextureRenderer.Render`, so the plugin overlay windows (ILT Hub, Item
  Info, ...) are drawn over the Avalonia panels.
* Input follows the new stacking: when ImGui is hovered or has an active widget,
  mouse messages go to ImGui instead of the Avalonia panel underneath
  (`Win32Backend.ImGuiOwnsMouseOverAvalonia`). An Avalonia drag/resize already
  in progress keeps its capture so the release is never lost.
* Nothing is submitted twice: the pending draw is cleared at the start of each
  ImGui frame and after submission.

## Live, per-category logging (engine + launcher)

### Engine

* New `LogSettings` reads engine.json:
  * `"LoggingLevel"`: global threshold, now including **Off**
    (Off, Error, Warning, Info, Debug, Trace; `Verbose`/`Warn` accepted).
  * `"LogCategories"`: per-category emit level (Off, Trace, Debug, Info) for
    General, D3D9, Compat, Render, Plugin, UI, Verbose. A category's lines are
    written at its level, so a Trace/Debug category set to **Info** shows in the
    normal log, and **Off** silences it.
* A background thread polls engine.json once a second and reloads on change
  (logs `LogSettings: engine.json changed - logging reloaded (...)`). Stopped in
  `EngineLifecycle.Shutdown` before unload.
* `RynthLog` category methods route through the category levels (the hard-coded
  `*Enabled` booleans are gone; defaults are unchanged: Render Off, Verbose Debug,
  everything else Info).
* Lines are tagged with their real level: `[DBG]` and `[TRC]` instead of `[INF]`.
* `Warn` is now hidden at level Error/Off; `Error` is always written.
  `LastIssue` is still recorded for filtered warnings.
* `EngineSettings.Save()` now copies through engine.json fields it doesn't own
  (e.g. `LogCategories`, `EnableDcompOverlay`) instead of dropping them.

### Launcher (Runtime tab)

* **Logging level** box gains **Off**, and applies immediately (writes
  engine.json on change; no Save needed). Unknown saved values fall back to Info.
* New **Logging** card, every change saved immediately and applied live:
  * Engine categories: Off / Trace / Debug / Info per category.
  * RynthAi categories: Off / Trace / Info per category (diagnostics.json).
  * RynthAi key events: Off / Trace / Info per event (diagnostics.json).
  * Reload button re-reads both files.
* New `LoggingSettingsStore` does the read-modify-write of both files
  (atomic temp-file swap for diagnostics.json, every other field preserved).

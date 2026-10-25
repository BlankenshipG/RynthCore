# Engine — plugin overlay windows while the ImGui shell is off

**Date:** 2026-10-04
**Branch:** `SK-local`
**Ships in:** installer 2026.10.4.9 (previous 2026.10.4.8)

## Problem
`EngineSettings.EnableImGuiShell` is hard-off unless the developer env var
`RYNTHCORE_FORCE_IMGUI=1` is set. `EngineFrameController.RunImGuiFrame` skipped both
`RynthCoreShell.Render` and `PluginManager.RenderAll`, so any plugin window without an Avalonia
panel could never show. That included RynthAi's ILT Hub (log: the Hub was created and the server
features came back, but no window) and its Item Info settings window.

## Added
- **Optional plugin export `void RynthPluginRenderOverlay()`** (`PluginContract.cs`,
  `PluginLoader.cs`, `LoadedPlugin.RenderOverlay`). With the ImGui shell off, the engine calls it
  every frame through the new `PluginManager.RenderOverlayAll()`. A plugin draws only its extra
  windows there, beside the Avalonia UI. Plugins without the export are unaffected. Their full ImGui
  UIs stay hidden, so nothing duplicates an Avalonia panel. The old ImGui bar stays hidden too.
- **Failure isolation:** if `RenderOverlay` throws, only that plugin's overlay windows are switched
  off for the session. The plugin is **not** marked `Failed`, so its tick, automation and Avalonia
  panels keep running. (`RenderAll` keeps its old behaviour.)
- **engine.json `"EnablePluginOverlayWindows"`** (default `true`) to hide the overlay windows.
  `EngineSettings.Save` writes it, and the launcher's `EngineJsonStore` preserves it.
  The windows need `EnableImGuiBackend` (default `true`).
- Same not-in-world guard as `RenderAll` (no drawing between logoff and the next login complete).

## Build
- `RynthCore.Engine` Release build succeeds (no new warnings). Shipped with RynthAi 0.6.15, which
  exports `RynthPluginRenderOverlay` (ILT Hub + Item Info windows).
- Not yet verified in game.

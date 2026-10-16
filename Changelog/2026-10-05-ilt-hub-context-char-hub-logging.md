# ILT Hub windows draw again, Char/Hub on the ImGui dashboard, hidden-log warning

**Date:** 2026-10-05
**Component:** RynthCore Engine (merge branch `merge/aelrynth-2026.10.5.1`, after installer 2026.10.5.2)

## Summary

Fixes three problems found testing installer 2026.10.5.2.

## ILT Hub (and the other RynthAi windows) never appeared

RynthAi logged `[Overlay] OnRenderOverlay skipped: no ImGui context.` on both clients.

- **Cause:** the merged engine handed plugins the ImGui context only when the ImGui shell is on.
  In this build the shell can only be turned on through an environment variable. RynthAi's own
  ImGui windows (ILT Hub, Mini Remote, Inventory HUDs, Item Info, translator window, nav overlay)
  draw only with that context, so they all skipped. UbRythai also refused to start without it
  (its `Initialize` returns 11).
- **Fix (`ImGui/EngineFrameController.cs`):** plugins also get the context when plugin overlay
  windows are enabled (`EnablePluginOverlayWindows`, default on) and the ImGui layer is on.
  The plugin host copies the context once at init, so plugin init waits for the first ImGui
  frame. The wait is capped at about 5 s (300 pump frames). If ImGui init fails or stalls,
  plugins start anyway and a warning is logged.
- The merged RynthAi reads the context only for those windows, so this does not switch it into
  its old legacy ImGui UI (the reason upstream withheld it).

## Char and Hub buttons were missing

With the ImGui layer on, the engine always shows upstream's ImGui RynthAi dashboard
(`RynthAiFace`) in the game window. Char and Hub had only been ported to the Avalonia dashboard,
which is no longer shown there.

- **Fix (`ImGui/Panels/RynthAiFace.cs`):** the launcher row now has 10 buttons. After Patrol:
  - **Char:** opens the ILT Hub (`/ra hub show`).
  - **Hub:** left-click toggles the Mini Remote (`/ra remote`); right-click opens the Inventory
    HUDs setup (`/ra huds`).

## Engine log nearly empty

The 2026.10.5.2 session logs had only loader lines and two warnings.

- **Cause:** `engine.json` had `LoggingLevel: Info` with every `LogCategories` entry at
  `Trace`. A category's value is the level its lines are written at, so every engine and
  plugin line became a Trace line and was dropped by the Info global level. The startup banner
  went through the same filter.
- **Fix (`EntryPoint.cs`, `LogSettings.cs`):**
  - The startup banner (build, OS/CLR, logging summary) is always written.
  - A new warning names any category set above the global level. It is logged at startup and
    whenever `engine.json` changes, e.g. `LogSettings: General=Trace, ... write above the
    global LoggingLevel (Info), so those lines are NOT logged.`
- Your `engine.json` now has `LoggingLevel: Trace` (changed at 09:10), so the next session
  logs everything as-is.

## Files

- `src/RynthCore.Engine/ImGui/EngineFrameController.cs`
- `src/RynthCore.Engine/ImGui/Panels/RynthAiFace.cs`
- `src/RynthCore.Engine/LogSettings.cs`
- `src/RynthCore.Engine/EntryPoint.cs`

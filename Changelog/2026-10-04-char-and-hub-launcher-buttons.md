# Launcher buttons: "ILT Hub" renamed to "Char", new "Hub" button (2026-10-04)

RynthAi 0.6.25, plus an engine change to the Avalonia RynthAi panel.

## What changed

The floating HUDs were hard to find: they start hidden and were only reachable through
Settings > Inventory Management or chat commands. Both dashboards now have a launcher for them.

| Dashboard | Char | Hub |
|-----------|------|-----|
| Avalonia RynthAi panel | Opens the ILT Hub (`/ra hub show`) | Left-click shows/hides the Mini Remote (`/ra remote`). Right-click opens the Inventory HUDs setup window (`/ra huds`). |
| ImGui dashboard launcher grid | Toggles the ILT Hub window. Only shown on ILT/ACECustom worlds, as before. | Toggles the Mini Remote. Lit while it is shown. Shown on every world. |

- Only the button labels changed. The ILT Hub window title, `/ra hub` and the `[ILT Hub]` chat
  messages are unchanged.
- In the ImGui grid, Hub uses the third cell of the last row, which was empty.

## Files

- Engine `UI/Panels/RynthAiPanel.cs`: "ILT Hub" launcher renamed to "Char"; new "Hub" launcher
  (row 3, column 2) with a right-click handler.
- `LegacyUi/LegacyDashboardRenderer.cs`: "Char" label, "Hub" grid button, and the
  `MiniRemoteVisible` / `SetMiniRemoteVisible` hooks.
- `RynthAiPlugin.cs`: wires the hooks to the HUD state at login and clears them at logout.
- Version 0.6.24 → 0.6.25.

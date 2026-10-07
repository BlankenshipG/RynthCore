# Avalonia RynthAi panel: Map and Lua buttons (2026-10-04)

Engine `RynthAiPanel` plus RynthAi 0.6.21. Ships in the next installer after 2026.10.4.16.

## Problem

The right halves of the split launcher buttons, **Lua** (next to Meta) and **Map** (next to
Nav), had no click handler, so they did nothing. Both windows are ImGui windows inside
RynthAi, and in Avalonia mode RynthAi's overlay render path never drew either of them.

There is no separate RynthLua plugin: the Lua Scripts editor is RynthAi's `LegacyLuaUi`,
controlled by `DashWindows.ShowLua`. So the Lua button follows the same pattern as Map and
the ILT Hub button.

## Engine (`UI/Panels/RynthAiPanel.cs`)

- **Map** sends the remote command `map toggle`, and **Lua** sends `lua toggle`. Both go through
  `RynthPluginApplyRemoteCommand`, the same path as the ILT Hub button. Both buttons have tooltips.
- `AddSplitLauncher` now returns its two half buttons.

## RynthAi 0.6.21

- New remote commands and chat commands `/ra map [show|hide]` and `/ra lua [show|hide]`. With no
  argument they toggle. They go through `HandleWindowCommand` to `LegacyDashboardRenderer.SetDungeonMapVisible` /
  `SetLuaWindowVisible`, which persist the choice and print a chat confirmation. Turning the map on
  while outdoors says it will appear indoors. Both commands are listed in the `/ra` help.
- `OnRenderOverlay` (Avalonia mode) now calls `RenderMapWindow(includeRadarAndChat: false)`.
  It draws only the dungeon map, because the Avalonia Radar panel and RynthChat already exist.
  ImGui-shell mode still draws all three.
- `RenderOverlayWindows` now also draws the Lua Scripts editor. Opening it rescans the
  `LuaScripts` folder.
- Fix: the dungeon map would not reopen after being closed with its title-bar X (its own `_open`
  flag stayed false and closed it again on the same frame) until you went outdoors and back.
  `DungeonMapUi.Render` now reopens whenever `ShowDungeonMap` is set.

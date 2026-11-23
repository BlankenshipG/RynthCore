# 2026.10.16.11 - Mini Remote: dock / undock / open undocked

## Why

Mini Remote already had the host title ↗/↙ pop-out, but there was no clear way to
open it undocked or toggle dock from Char / Options / chat. Undocked is the
comfortable place for the expanded Mini Remote Options (sections, layout, slot size).

## Changes

- `/rc miniremote undock` (aliases: `popout`, `float`): open on Remote and pop out.
- `/rc miniremote dock` (alias: `redock`): dock back, or open docked if closed.
- Char right-click menu: **Mini Remote (undocked)**.
- Mini Remote sections menu: **Undock Mini Remote** / **Dock Mini Remote**.
- Helpers: `MiniRemoteFace.OpenUndocked` / `DockOrOpen`.

Paired with RynthAi: Float/Dock beside Options; undocked Options left-click opens
the expanded section popup.

## Files

- `Compatibility/RynthCoreChatCommands.cs`
- `ImGui/Panels/MiniRemoteFace.cs`
- `ImGui/Panels/MiniRemoteFace.Sections.cs`
- `ImGui/Panels/RynthAiFace.cs`

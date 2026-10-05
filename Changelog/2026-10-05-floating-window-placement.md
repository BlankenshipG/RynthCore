# Floating windows open where they can be seen (RynthAi 0.5.5-legacy-ui)

Date: 2026-10-05
Branches (local): RynthCore `merge/aelrynth-2026.10.5.1`; RynthSuite `fix/rynthai-launcher-remote-commands`
Previous plugin version: 0.5.4-legacy-ui

## Problem

New windows with no saved position opened in the top-left corner of the game view:

- the Pets window at ImGui's default (60,60);
- the Item HUD at (20,220);
- the Mini Remote at (20,120);
- the Dungeon Map in the panel cascade (100,100 and onwards).

Popped-out panels are separate Windows windows above the game window. They often sit in that
corner, and nothing drawn inside the game frame (ImGui windows included) can appear over them. So
the new windows opened hidden underneath. The draw-order fix (`2026-10-05-imgui-over-avalonia-port.md`)
only covers the Avalonia layer painted inside the game frame.

## Changes

### RynthAi (plugin)

- New `UiPlacement` helper:
  - `CenterFirstUse(offset)`: before `Begin`, sets a `FirstUseEver` position centred in the game
    view (pivot 0.5,0.5). It only applies when imgui.ini has nothing saved for the window.
  - `RescueOncePerShow(ref checked)`: once per show, moves the window to the middle if less than
    40 px of it is on screen. The test is the ILT Hub's existing off-screen check, now shared as
    `UiPlacement.IsOnScreen`.
- First-use spots (centre plus offset):

  | Window | First-use spot |
  | --- | --- |
  | ILT Hub | centre |
  | Pets | centre |
  | Quests (popped out) | centre + (40,40) |
  | Item HUD | centre + (220,-160) |
  | Mini Remote | centre + (-220,-160) |

  All five also get the off-screen check each time they're shown.
- The ILT Hub's off-screen rescue now moves the window to the middle of the game view instead of
  (40,40), which is where popped-out panels usually sit.

### Engine

- `PanelSpec.OpenCentered`: a panel with nothing saved and no `DefaultPos` opens centred in the game
  view instead of the cascade. The Dungeon Map uses it.
- Docked panels open clear of popped-out windows.
  - When an engine panel opens docked, `ImGuiPanelHost` checks the visible popped-out windows
    (`LayeredWindow.VisibleContentRects`: every Avalonia and ImGui pop-out and the floating bar).
  - If more than half of the panel would be hidden, it opens at the nearest spot on a 9x9 grid over
    the game view that's hidden the least. The open's normal save records that spot.
  - Each move is logged as `ImGuiPanelHost: <title> would open under a popped-out window; moved from
    (x,y) to (x,y).`

## Limits

- Plugin windows (Pets, Quests, Item HUD, Mini Remote, Hub) are centred on first use and pulled back
  if off-screen. They don't yet avoid popped-out panels: the plugin can't see where those are. That
  would need a new PluginSdk host call.
- ImGui windows still can't draw above popped-out panels. To keep both visible, dock the panel, or
  move it away from where the windows open.

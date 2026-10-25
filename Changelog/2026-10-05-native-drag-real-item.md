# Mini Remote: dragging from the game inventory uses the item you dragged

**Date:** 2026-10-05
**Components:** RynthCore.Engine (next installer 2026.10.5.11), RynthAi 0.5.12-legacy-ui (previous 0.5.11-legacy-ui)

## Problem

Dragging an item from AC's own inventory onto a Mini Remote slot assigned whatever item was
selected before the drag, not the item being dragged. AC does not change the selected object
when a drag starts, and the engine's drag bridge read the dragged item from the selection.

Log evidence (session pid 33152): three drags of different items all published
`0x803AAE1B` (Frozen Valley Everlasting Portal Gem, the last clicked item), which landed in slot 17.

## Fix

- **New `Compatibility/DragDropHooks.cs`** hooks `UIElementManager::StartDragandDrop`
  (`0x0045E120`, thiscall). It resolves the dragged UI element to an object id with AC's own
  `UIElement_ItemList::InqDropIconInfo` (`0x004E3380`, cdecl). Same approach as Chorizite's UIHooks.
  - Both addresses are resolved by byte patterns that are unique in the shipped client. If either
    pattern misses, the hook is skipped (no hard-coded fallback) and the old behavior remains.
- **`ItemDragBridge`**
  - `OnNativeDragStart(itemId)` takes the hooked item while a game-owned left press is held, for
    carried items only (pack, side pack or worn). Spell-bar drags are ignored.
  - Hooked drags are published as `RYNTH_INV_ITEM`, so they can go on **any** slot (and replace an
    assigned one), exactly like drags from the RynthCore Inventory.
  - The selection guess (`RYNTH_GAME_ITEM`, empty slots only) now runs only when the hook is not installed.
  - Released over an ImGui window, the drop is still handed back to AC at the drag's start, so the
    item never lands on the ground.
- **RynthAi** — `MiniRemoteHud` comment updated to describe both payloads; version 0.5.12-legacy-ui.

## Verifying

The engine log shows, at startup:

- `HookResolver[DragDrop.InqDropIconInfo]: RESOLVED via pattern-unique @ 0x004E3380`
- `HookResolver[DragDrop.StartDragandDrop]: RESOLVED via pattern-unique @ 0x0045E120`
- `Compat: drag-start hook ready`

Then, on each AC inventory drag:

- `DragDropHooks: AC drag start item=0x........` (first 20 drags)
- `ItemDragBridge: native inventory drag 0x........ published to ImGui.`

## Unchanged

- Click an item (game inventory or RynthCore Inventory), then click an empty Mini Remote slot.
- RynthCore Inventory right-click **Add to Mini Remote**.
- Drag from the RynthCore Inventory or a popped-out Inventory.

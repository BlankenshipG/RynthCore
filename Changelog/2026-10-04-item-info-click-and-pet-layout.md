# Item info: ID on left/right click + pet-style layout — RynthAi 0.6.29

**Date:** 2026-10-04
**Component:** RynthCore.Plugin.RynthAi 0.6.28 → 0.6.29

## What's new

### ID + print on click
- New **"ID + print on click"** option: **Off / Left mouse click / Right mouse click**.
- Clicking an item in the world or in your packs IDs it if needed and prints its info to chat.
  It also prints for an item that was already selected; the old "on select" option only fired when the selection changed.
- Ignored:
  - clicks on RynthCore / ImGui windows;
  - drags (moving more than 6 px while held, e.g. dragging items between packs);
  - holds longer than 0.6 s (right-button camera turning).
- The info is printed for the item that is selected 150 ms after the button is released.
  With **Right mouse click**, if the client doesn't change the selection on a right click, it prints the
  currently selected item. That still works as left-click to select, right-click to print.
- The weapon / armor / jewelry / other type filters apply to both the select and click triggers.
- If on-select and on-click both fire for the same item, it prints once (1.5 s duplicate guard).
- Requires the ImGui overlay (clicks are read from the overlay's input state). Works with both
  the ImGui dashboard and the Avalonia UI overlay path.

### Pet-style layout for weapons and armor
- New **Layout** option: **Pet-style lines** (new default) or **One line (Mag-Tools)** (the previous output).
- Pet-style mirrors the ILT Hub pet roster: a name/identity line, then indented detail lines:

  ```
  [RynthAi] Gold Ornate Long Sword (Slash Sword), Noble Relic Set, Tinks 4, Applied: Steel x2, Craft 8
  [RynthAi]   CS, Undead Slayer, 152.48-236, 0.35v, 18%a, 12%md
  [RynthAi]   [D 3, CD 2]  Wield Lvl 180, Heavy Weapons 375, Diff 270
  [RynthAi]   Spells: Legendary Blood Thirst
  ```

  - Line 1: name (damage type + mastery), set, AL, tinks, applied materials, craft / salvage work, keyring.
  - Line 2: imbues, armor cleave, crit stats, splits, range, cleaving, slayer, damage, variance,
    elemental / damage-mod bonuses, %a / %md / %mgc.d / %msl.d / %mc, unenchantable protections.
  - Line 3: ratings cluster, then wield / activation / difficulty requirements and value / burden.
  - Line 4: spells.
  - Empty lines are skipped, so plain armor prints only one or two lines.
- New **Detail lines colour** (chat type for lines 2–4), default "Same as name line", with a Test button.
- Every existing field and rating toggle, the spell mode, the prefix and the chat type apply to both layouts.
  The one-line output is identical to 0.6.28.

## Where to set it
- **Advanced Settings → Display → Item Info**: "ID + print on click" and "Layout" combos.
- **Item Info window** (`/ra iteminfo settings`): click trigger in *When to print*, layout and
  detail colour in *Chat output*. The preview shows the multi-line layout.
- Commands:
  - `/ra iteminfo click left|right|off`
  - `/ra iteminfo layout pet|line`

## Implementation notes
- `MagItemDescriber.Build` now returns a `MagItemDescription` (title plus fields tagged with a
  `MagItemSection`, in Mag print order). `ToOneLine()` reproduces the classic line and
  `ToPetLines()` groups the same fields by section. `Describe()` is kept as a one-line wrapper.
- Click detection (`DetectItemInfoClick`) runs on the render thread in `OnRender` /
  `OnRenderOverlay`, edge-detecting `io.MouseDown`. The selection is read on the plugin tick
  (`ResolveItemInfoClick`, handed over with `Interlocked`), so all describe / ID state stays on one thread.
- New `MagItemInfoSettings` fields: `ClickTrigger`, `Layout`, `DetailChatType` (sanitized and included in reset).

## Files
- `Plugins/RynthCore.Plugin.RynthAi/ItemInfo/MagItemDescriber.cs`
- `Plugins/RynthCore.Plugin.RynthAi/ItemInfo/MagItemInfoSettings.cs`
- `Plugins/RynthCore.Plugin.RynthAi/RynthAiItemInfo.cs`
- `Plugins/RynthCore.Plugin.RynthAi/RynthAiPlugin.cs` (render hooks, on-select dedupe, VersionPointer 0.6.29)
- `Plugins/RynthCore.Plugin.RynthAi/RynthAiCommands.cs` (help line)
- `Plugins/RynthCore.Plugin.RynthAi/LegacyUi/LegacyItemInfoUi.cs`
- `Plugins/RynthCore.Plugin.RynthAi/LegacyUi/LegacyAdvancedSettingsUi.cs`
- `Plugins/RynthCore.Plugin.RynthAi/RynthCore.Plugin.RynthAi.csproj` (0.6.29)

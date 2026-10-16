# Floating HUDs: item count HUD, Mini Remote, quest favorites (2026-10-04)

RynthAi 0.6.23, plus an engine Settings button. Ships in the next installer after 2026.10.4.17.

## What changed

Four new floating windows, modelled on UtilityBelt's Item Hud, Mini Remote and quest
favorites. They are ordinary ImGui windows. You can dock them, undock them, or drag them
outside the AC window, because the engine has docking and multi-viewport enabled. Each HUD
remembers its own position.

| Window | Controlled from | Command |
|--------|-----------------|---------|
| Inventory HUDs (setup) | Advanced Settings > Inventory Management > **Inventory HUDs...**, and the same button in the Avalonia Settings panel | `/ra huds [show\|hide]` |
| Item count HUD | Inventory HUDs setup window | `/ra itemhud [show\|hide]` |
| Mini Remote | Inventory HUDs setup window | `/ra remote [show\|hide]` |
| Quest favorites HUD | ILT Hub > Character > Quest tracker | `/ra quests favhud [show\|hide]` |
| Quests (pop-out tracker) | ILT Hub quest tracker **Pop out** button | `/ra quests window [show\|hide]` |

Running a command without `show` or `hide` toggles the window.

### Item count HUD

- Shows one row per pinned item: the item's icon (read from portal.dat) and how many you
  carry, with stacks added up across all packs. The count turns red at 0. Names are optional.
- Right-click the HUD to show names, lock its position, change the icon size (12–48 px),
  open the setup window, or hide it.

### Mini Remote

- **Stats** (ILT-like worlds): session time, plus XP, Luminance, kills, coins and pyreals per hour.
  **Reset XP** restarts the session rates. **Report** prints a summary to local chat.
- **Pet**: the essence that is out ("Out" / "Healing"), or the next combat essence
  ("Ready" / "Empty").
- **Item gems**: a 5×6 grid of item slots. Click a slot to use that item; the slot dims when
  you carry none. Right-click a slot to assign the selected item or clear it.
- **Toggles**: Macro, Combat, Buff, Nav, Loot, Meta, Pet (summon pets).
- **Bank** (ILT-like worlds): P, Lum, LK, MK, EC, WE from the ILT Hub bank tracker.
- **Rebuff**: FB (force rebuff) and CFB (cancel force rebuff).
- Right-click the window background to hide sections, lock its position, open the setup
  window, or hide it.

### Inventory HUDs setup window

- Show/hide the item HUD (with or without names) and the Mini Remote.
- **Scan pack**, **Add from inventory** (pins the selected item), **Drop missing** (unpins
  items you no longer carry).
- An **On HUD** list (Up/Down to reorder) and a filterable **Not on HUD** list of everything in
  your pack, with `<` / `>` to move items between them. Double-click an item in Not on HUD to
  pin it.
- Mini Remote slot picker with **Set** (from the selected item), **Clear** and **Refresh**.

Item HUD and Mini Remote settings are saved per character in `huds.json`.

### Quest favorites

- The ILT Hub quest tracker has a new star column. Click `*` to favourite a quest.
  **Favorites only** filters the table to starred quests.
- **Show floating favorites HUD** opens a small HUD listing starred quests, with ready /
  time-left. Right-click it to lock it, open the quest tracker, refresh `/myquests`, or hide it.
- **Pop out** moves the quest tracker into its own "Quests" window. Closing that window docks
  it back into the Hub.
- Favorites and HUD flags are saved per character in `ilt-hub.json` (`Character.QuestFavorites`).

## Not carried over from UtilityBelt

These UtilityBelt Mini Remote and Quests features were left out:

- Storm, Chat XL, Guardian/Translate, the profile combos and the Fellow toggle. RynthAi has no
  equivalent systems for these yet.
- The L / LumS / KillL / PassL / C / Conv stat cells.
- The quest rewards editor.

## Files

- New `Huds/` folder:
  - `HudController.cs`: pack scan, pump-thread actions, commands.
  - `HudState.cs`: settings and `huds.json` store.
  - `HudIconCache.cs`: decodes portal.dat icons and uploads D3D9 textures.
  - `DxtUtil.cs`: DXT decoder.
  - `HudDraw.cs`, `ItemCountHud.cs`, `MiniRemoteHud.cs`, `HudSetupUi.cs`.
- `IltHub/IltQuests.cs`: favorites, favorites HUD, pop-out window.
- `IltHub/IltHubState.cs`, `IltHubStore.cs`: quest favorite fields.
- `IltHub/IltHubUi.cs`, `IltHubController.cs`: floating window rendering and the `quests` verbs.
- `IltHub/IltSessionRates.cs`: `RateSummary` for the Mini Remote.
- `IltHub/IltPets.cs`: Mini Remote pet line.
- `LegacyUi/LegacyAdvancedSettingsUi.cs`, `LegacyDashboardRenderer.cs`: Inventory HUDs button.
- `RynthAiPlugin.cs`, `RynthAiCommands.cs`: wiring and `/ra` help.
- `Diagnostics/RynthLog.cs`: new `Huds` log category.
- Engine:
  - `UI/Panels/SettingsPanel.cs`: Inventory HUDs button in Inventory Management.
  - `UI/Panels/RynthAiPanel.cs`: `SendRynthAiCommand` helper.
- RynthAi version 0.6.22 → 0.6.23.

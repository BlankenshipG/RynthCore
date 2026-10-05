# Items panel: Shields section and "Add Selected Shield" (2026-10-04)

Engine Avalonia `ItemsPanel` plus RynthAi 0.6.20. Ships in the next installer after 2026.10.4.16.

## Problem

RynthAi stores off-hand shields in its item list (`ItemRule.Action = "Shield"`), and its
ImGui Items tab has a Shields section. The engine's Avalonia Items window (the one
opened from the dashboard) never got that section:

- "Add Selected Weapon" rejects shields with "That is a shield — use Add Selected Shield",
  but that button didn't exist in the engine panel, so shields couldn't be added there.
- Shields already in the list showed up under Weapons with an element picker.

## Engine (`UI/Panels/ItemsPanel.cs`)

- New **Shields (off-hand)** section between Weapons and Consumables:
  - an "Auto-equip with one-handed melee weapons" ON/OFF toggle with a tooltip (shown when the
    plugin reports the setting);
  - a shield list with a Del button on each row;
  - **Add Selected Shield**, which calls the plugin's new `RynthPluginAddSelectedShield` export.
    With an older plugin the click does nothing and logs that RynthAi 0.6.20+ is required.
- The Weapons section now lists only non-shield entries.
- `WeaponEntry` carries `action` and the payload carries `autoEquipShield`; both are part of
  change detection, so plugin-side edits appear within the 1 s poll.
- Deletes now remove the row's own entry instead of a list index, because the weapon and
  shield rows are filtered views of one list.

## RynthAi 0.6.20

- New export `RynthPluginAddSelectedShield`, which goes through `LegacyDashboardRenderer.AddSelectedShield`
  to the existing `LegacyWeaponsUi.AddSelectedShield` and then saves settings.
- `ItemsBridgePayload.AutoEquipShield` (`bool?`) is sent to the panel. It's applied on write-back
  only when present, so older engine panels leave the setting alone.
- `ApplyItemsJson`:
  - new entries keep the `Shield` action the panel sends (anything else becomes `Weapon`);
  - deleting a shield from the engine panel clears any monster off-hand that pointed at it and
    saves the monsters file (the same cleanup as the ImGui Del button).
- Version bumped to 0.6.20 (`csproj`, `RynthAiPlugin.VersionPointer`).

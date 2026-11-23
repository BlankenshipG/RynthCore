# 2026.10.16.4 - Inventory: Item Count and Global Inventory buttons

Previous: 2026.10.16.3.

## Change

Inventory opens the related windows from its own chrome instead of hunting through
menus:

- Modern header: Bag (Item Count) and Globe (Global Inventory) next to Classic / Close.
- Classic title bar: Count and GInv text buttons (with Vault / size / tools).
- Character menu: Global Inventory line (`RynthInventory/Main`, same as `/ginv`).
- Settings > Inventory Management > Floating HUDs tooltip clarifies the floating
  pack item count HUD (`/ra huds`) is not the Item Count panel (`/rc itemcount`).

Item Count toggles via `PanelRouter` (`/rc itemcount`). Global Inventory toggles the
RynthInventory script window, or runs `/ginv` when the plugin is not loaded yet.

## Files

- ImGui/Panels/InventoryFace.cs
- ImGui/Panels/InventoryFace.Classic.cs
- ImGui/Panels/RynthAiFace.cs
- UI/Data/SettingsData.cs

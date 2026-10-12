# Items panel: Remove Missing button

Engine 2026.10.5.34 (previous: 2026.10.5.33). Pairs with RynthAi 0.5.43-legacy-ui.

## What changed

- `ImGui/Panels/ItemsFace.cs`: a "Remove Missing" button at the top of the Items panel, above the
  lists (which can run long), with a tooltip. `AddSelected` takes an optional tooltip.
- `UI/Data/ItemsData.cs`: `ItemsCommands.CleanMissing()` calls RynthAi's `RynthPluginCleanItems`
  export, then refreshes the panel.
- What RynthAi does when the button is clicked:
  - removes listed weapons and consumables that are no longer in your inventory;
  - re-points an entry at an unlisted item of the same name, when your pack has one;
  - does nothing while your inventory is still loading;
  - reports in chat what it removed and re-pointed.
- With a RynthAi older than 0.5.43 the click is logged and nothing happens.

The engine builds in Release. Not yet tested in game.

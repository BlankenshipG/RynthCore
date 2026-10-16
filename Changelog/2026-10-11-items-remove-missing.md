# Items panel: Remove Missing button

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
- With a RynthAi that lacks `RynthPluginCleanItems`, the click is logged and nothing happens.

The engine builds in Release. Not yet tested in game.

## Installed

- 2026-10-11 22:41: published to `C:\Games\RynthCore\Runtime` (FileVersion 2026.10.5.34). Both
  running clients (pids 33684 and 61616) hot-reloaded to it (gen 3) without errors.
- 2026.10.5.33 is saved in `C:\Games\Rynthcore\PreviousReleases\2026.10.5.33-engine` and
  `C:\Games\_rynth-backups\20261011-pre-items-clean\Runtime`.

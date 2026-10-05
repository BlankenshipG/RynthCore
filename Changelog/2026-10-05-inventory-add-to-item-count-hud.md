# Inventory: "Add to item count HUD" (RynthAi 0.5.3-legacy-ui)

Date: 2026-10-05
Branches (local): RynthCore `merge/aelrynth-2026.10.5.1`; RynthSuite `fix/rynthai-launcher-remote-commands`
Previous plugin version: 0.5.2-legacy-ui

## What's new

- The in-client **Inventory** panel's right-click menu has **Add to item count HUD** for any item
  that isn't a pack. It adds the item, by name, to RynthAi's floating item count HUD and shows the
  HUD. You no longer have to select the item in the game first and use the HUD setup window.
- Typed equivalent: `/ra itemhud add <item name>`.
- RynthAi confirms in chat: `Added <name> to the item count HUD.` or `<name> is already on the item
  count HUD.` The icon and WCID fill in on the HUD's next pack scan.

## How it works

- Engine `InventoryFace` sends the remote command `itemhudadd=<item name>` through
  `RynthAiCommands.ApplyRemoteCommand`.
- RynthAi's `ApplyRemoteCommand` queues it to the pump thread and calls
  `HudController.AddToItemHudAndShow` (new). `/ra itemhud add` uses the same handler.
- `/ra itemhud [show|hide|toggle]` is unchanged.

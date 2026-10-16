# Mini Remote: attack target and summon health / time left

**Date:** 2026-10-05
**Component:** RynthAi plugin (RynthSuite `Plugins/RynthCore.Plugin.RynthAi`)
**Version:** 0.6.30 → 0.6.31

## What's new

The Mini Remote HUD has two new live rows.

- **Target:** the creature combat is attacking, with its name, distance in yards
  and a health bar (green, yellow or red). It reads "Target: none" when combat has
  no target, and the bar shows "HP --" until the first health update arrives.
- **Summon:** while your pet is out, shows its name, a health bar and the time
  left, with a countdown bar that turns yellow under 90 s and red under 30 s. A
  "Healing" tag appears while the ILT Hub heal pet is working. When no pet is out,
  the row shows the ILT Hub's next combat essence with Ready / Empty, as before.

Both rows can be switched off from the Mini Remote's right-click menu ("Attack
target" and "Pet / summon").

## How it works

- A new `CombatHudTracker` runs on the pump thread and publishes an immutable
  snapshot for the HUD. It only runs while the Mini Remote and one of these rows
  are shown.
- **Target:** comes from `CombatManager.activeTargetId`. Health uses the object
  cache's health ratio, which the server already pushes during the fight.
- **Summon:** found as "&lt;Player&gt;'s &lt;Pet&gt;" in the landscape.
  - **Time left:** the new pet is appraised once (up to 3 tries, 5 s apart), and
    its `RemainingLifespan` (268) anchors a local countdown. `Lifespan` (267) sets
    the bar's full length.
  - **Fallback:** if the server never reports a lifespan, the row shows how long
    the pet has been out instead.
  - **Health:** polled with `QueryHealth` every 3 s. The server streams health for
    only the most recently queried creature, so each pet poll is followed by a
    re-query of the combat target (or your selected target). This keeps combat's
    damage detection working.

## Files

- New: `Huds/CombatHudTracker.cs`
- Modified: `Huds/MiniRemoteHud.cs`, `Huds/HudController.cs`, `Huds/HudState.cs`,
  `RynthAiPlugin.cs`, `RynthCore.Plugin.RynthAi.csproj`

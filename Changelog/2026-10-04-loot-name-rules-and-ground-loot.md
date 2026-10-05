# Looting fix (name rules never matched) + ground-item looting

**Date:** 2026-10-04
**Components:** RynthAi plugin 0.6.27 (RynthSuite)

## Problem: SilentKelpie looted nothing

SilentKelpie's log for this session (pid 30364) shows 21 corpses claimed, opened and fully
evaluated. All 225 items evaluated across two sessions came out `-> leave`. That includes items
the active profile (`T10locky.utl`) explicitly keeps by name, such as "Thaelaryn Core" via its
`(T) Thaelaryn Core` rule.

### Cause
VTank name rules (node type 1, `StringValueMatch` on key 1 = Name) and native JSON name rules
read the name through `WorldObject.Values(StringValueKey.Name)`. That goes to the engine's
string-property read, which off the main thread only answers from the appraisal cache. ACE does
not mark `PropertyString.Name` as an assessment property, so appraisal data never contains it.
Because the loot evaluator runs on the plugin pump thread, every name read returned `""` and
every name rule failed. The object's own `Name` (from the network snapshot, which is what the
`[LootEval]` log line prints) was correct all along.

### Fix
`Combat/AcStubs.cs` `WorldObject.Values(StringValueKey, ...)`: when the live read is empty and
the key is `Name`, return the snapshot `Name`. This affects every consumer of name rules: corpse
loot (VTank and native profiles), `/ra lootcheck`, AutoVendor rules, ILT equip suits and meta
expressions.

## New: loot items on the ground (UB-IT AutoGroundLoot)

- **Setting:** Advanced Settings > Looting > **Loot Items On Ground** (`EnableGroundLoot`, default
  off; saved with the profile).
- **Command:** `/ra groundloot on|off|status|scan`. `scan` clears the skip list.
- **Range:** the corpse max range (`CorpseApproachRangeMax`, yards, Ranges tab).
- **Rules:** the active loot profile, through the same `ClassifyItemAgainstProfile` corpses use.
  This covers VTank `.utl` and native `.json` profiles, mana stone and mana-tap rules, and
  salvage items queued for salvaging.
- **What counts as a ground item:** readable ownership with no container and no wielder, plus a
  world position within range. Scenery, creatures, players, NPCs, vendors, portals, doors,
  corpses, lifestones, signs, housing, containers, combat pets and services are excluded.
- **Appraisal:** stat-gated rules request an appraisal first (two per scan). If none arrives
  within 3 s, the item is classified best-effort, matching the corpse assess window.
- **Pickup:** only when the activity arbiter hands Looting the tick, so navigation is already
  stopped. It sends `MoveItemExternal(item → player)`, and ACE walks the character over and picks
  the item up. The pickup is confirmed when the item gets a container, is retried up to 3 times
  over 5 s windows, and is then skipped for 5 minutes. Non-matching items are re-checked after
  60 s.
- **Priority:** corpses come first. Ground loot runs only when no corpse is claimed or open. A
  ground pickup already sent finishes before the next corpse is claimed.
- **Logging:** every ground evaluation is logged as `[LootEval] ground '<name>' ... -> KEEP|leave`,
  and claims and pickups as `Ground loot: ...`.

## Files
- `Combat/AcStubs.cs`: Name fallback.
- `Combat/WorldObjectCache.cs`: new `IsOnGround(id)`.
- `GroundLootController.cs` (new): scan, claim, pickup, confirm, `/ra groundloot`.
- `CorpseOpenController.cs`: ground loot hooked into `TickCorpseOpening`; `HasLootWork` includes
  ground matches.
- `LegacyUi/LegacyUiSettings.cs`, `LegacyDashboardRenderer.cs`, `LegacyAdvancedSettingsUi.cs`:
  `EnableGroundLoot` setting and checkbox.
- `RynthAiPlugin.cs`, `RynthAiCommands.cs`: command registration and help; ground state cleared
  on logout.

## Notes
- `T10locky.utl` has no Pyreal rule (its only coin rule matches "Coin"), so Pyreals are still left
  on corpses. That is how the profile is set up, not a bug.
- Versions: RynthAi `0.6.26` → `0.6.27`.

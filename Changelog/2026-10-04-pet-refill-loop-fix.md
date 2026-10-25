# Pet essence refill loop fix — RynthAi 0.6.28 + engine

**Date:** 2026-10-04
**Components:** RynthCore.Engine (property caches), RynthCore.Plugin.RynthAi 0.6.27 → 0.6.28

## Problem

SilentStorm got stuck refilling the same pet essence (0x9757B329) over and over. The log
(`RynthCore.9568.log.old`) showed `Pet: refilling essence … with spirit …` about every 7 s,
and every 30 s once cooldowns kicked in. Each attempt used an Encapsulated Spirit on the essence.
The server switches the player to peace mode and plays the clap animation for every refill,
so the character kept dropping out of combat.

## Root cause

- `PetManager` decides whether an essence is empty by reading its `Structure` (charges) through
  `TryGetObjectIntProperty`. The plugin pump runs off the main thread, so that read was
  served **only** from the appraisal cache. That cache is a snapshot from the last time the item was ID'd.
- When the server refills an essence it sends a `PublicUpdatePropertyInt(Structure = MaxStructure)`.
  The engine stored that in the property-update cache, but the off-thread read never consulted
  that cache, and the appraisal snapshot was never patched. So the essence kept reading 0 charges.
- The server's reply ("You add the spirit to the essence." / "This essence is already full.")
  sent `PetManager` back to Idle without recording anything. On the next tick it read 0 again
  and issued another refill.

## Fix

### Engine
- `PropertyUpdateHooks.CacheInt` / `CacheBool` now also patch the appraisal cache entry for that
  object, if one exists (`AppraisalHooks.PatchCachedInt` / `PatchCachedBool`). Off-thread reads
  now see live server updates (Structure after a refill or summon, etc.) instead of the last ID's value.
- `ClientObjectHooks.TryGetObjectIntProperty`: off the main thread, an appraisal-cache miss now
  falls back to the property-update cache (a thread-safe managed dictionary) instead of failing.

### RynthAi plugin (also protects against older engines)
- The server's success and already-full replies now mark the essence as confirmed full for up to 30 min.
  While that mark is set, a stale 0-charge read is ignored and the essence is used for summoning.
  The mark clears as soon as a summon reports "not enough charges".
- After a confirmed refill the plugin requests a fresh ID of the essence, so the cached charges and the
  ILT pet roster agree.
- Loop guard: once an essence has been refilled twice within 2 min and still reads empty, it
  is parked for 5 min with a log line, rather than burning more spirits.
- New log lines: `Pet: refill complete (essence …, server confirmed).`,
  `Pet: essence … is already full; charge read was stale.`, and the parking message.

## Files
- `RynthCore/src/RynthCore.Engine/Compatibility/AppraisalHooks.cs`
- `RynthCore/src/RynthCore.Engine/Compatibility/PropertyUpdateHooks.cs`
- `RynthCore/src/RynthCore.Engine/Compatibility/ClientObjectHooks.cs`
- `RynthSuite/Plugins/RynthCore.Plugin.RynthAi/Combat/PetManager.cs`
- `RynthSuite/Plugins/RynthCore.Plugin.RynthAi/RynthCore.Plugin.RynthAi.csproj` (0.6.28)
- `RynthSuite/Plugins/RynthCore.Plugin.RynthAi/RynthAiPlugin.cs` (VersionPointer 0.6.28)

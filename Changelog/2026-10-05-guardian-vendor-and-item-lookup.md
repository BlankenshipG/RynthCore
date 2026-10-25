# ILT Guardian: hand-in finds the vendor and the item in your pack

**Date:** 2026-10-05
**Component:** RynthAi 0.5.13-legacy-ui (previous 0.5.12-legacy-ui), `IltHub/IltGuardian.cs`

## Problem

The riddle was translated correctly, but the hand-in never got further:

- **Temple guardian** (Myrrh, Saffron): every attempt failed within ~30 ms with
  "no Myrrh carried and no vendor nearby", while Archmage Yuzai Zhen (`0x77F15002`) stood next to you.
- **Attribute guardian** (Ashwood Talisman): after buying the talisman, every attempt failed with
  "no Ashwood Talisman in your packs".

## Causes

1. The vendor search looked for `ObjectClass == Vendor`. The world cache classifies every creature,
   vendors included, as Monster (or Npc later); nothing is ever classed Vendor, so no vendor could match.
2. The item search used the world cache's inventory set. That set is filled asynchronously (a fresh
   purchase can be missing), and items from an open vendor's stock land in it too (they have no
   position), so it isn't a reliable "what's in my pack" list.

## Fix

- Vendors are recognised by the weenie **BF_VENDOR** bitfield flag (`0x200`) through
  `TryGetObjectBitfield` (a cache entry classed Vendor still counts).
- The hand-in item is looked up in a **live walk of your pack and side packs**
  (`GetDirectInventory(forceRefresh: true)`, AC's container contents with live names). Worn items are skipped.
- Item and vendor-stock names are compared ignoring case and extra spaces.
- When something isn't found, the IltHub log says why:
  - `[IltGuardian] no '<item>' among N carried items; similar names: ...`
  - `[IltGuardian] vendor search: N landscape objects, bitfield=yes, vendors: '<name>' 0x... <dist> m`
  - `[IltGuardian] vendor for '<item>': <name> 0x...` when a vendor is picked.

## Not changed

- Buying applies to the Temple guardian only (Attribute guardian items must be carried).
- Search range stays 60 m for the vendor and the guardian.

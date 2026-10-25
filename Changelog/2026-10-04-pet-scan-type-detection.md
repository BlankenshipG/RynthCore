# Pet scan: detect pets by type, not by "Essence" in the name

**Date:** 2026-10-04
**Components:** RynthAi plugin 0.6.26 (RynthSuite), RynthCore Engine / PluginSdk (next installer, 2026.10.4.19)

## Problem

The ILT Hub **Pet** tab (roster, **Scan pack**, heal-pet lookup, Mini Remote pet line) counted
any carried item whose name ended in ` Essence` as a combat pet. Augmentation gems and other
non-pet items with that suffix (for example "Jibril's Essence") showed up in the roster and got
appraised by Scan pack.

## Fix

### RynthAi — `IltHub/IltPets.cs`
- New `IsPetDevice(WorldObject)` decides by item type:
  - **Never pets:** Encapsulated Spirit, the charm block (78780030–78780089), capture
    devices (78780001–78780012) and the summoned combat pet creature block (787802001–787802072).
  - **Pets:**
    - the ACECustom combat essence WCID block (787801001–787801072);
    - the summoning-gem icon underlay (DID 0x06007420), which needs no appraisal;
    - `UseRequiresSkill` / `UseRequiresSkillSpec` = Summoning (54);
    - ACECustom `BondLevel` (9053) > 0 or `PotencyStored` (9056) present.
  - **Name hints only nominate items.** A name ending in " Essence", Healing Buddy / Dule box,
    stamina buddy/crate/pet, or cosmetic/plush/display pet only makes an item a candidate.
    It counts as a pet once it shows charges (`MaxStructure` > 0), matching UtilityBelt's pet scan.
- `ClassifyKind` now requires `IsPetDevice`. After that, naming picks the type: heal naming
  → Healing, cosmetic naming → Cosmetic, otherwise Combat. Manual re-types ("Add sel." /
  right-click) and the ticked heal pet still win.
- **Scan pack** appraises every confirmed pet and also any un-appraised pet-named candidate,
  so its type can be confirmed or ruled out. The chat line reports both counts. The 2 s
  background trickle also appraises candidates, two per scan.

### Engine / SDK
- `ClientObjectHooks.TryGetObjectDataIdProperty` now serves `IconOverlay` (50, PWD+20) and
  `IconUnderlay` (52, PWD+24) as well as `Icon` (8). This is the same network-populated
  PublicWeenieDesc read, so it works on never-appraised pack items. No API version change:
  older engines just return false for 50/52, and the plugin falls back to the appraisal-based
  signals.

## Notes
- Without the new engine, a real pet whose WCID is outside the essence block appears once its
  appraisal lands (via Scan pack or the background trickle) instead of right away.
- Versions: RynthAi `0.6.25` → `0.6.26`.

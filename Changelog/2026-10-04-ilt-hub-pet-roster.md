# ILT Hub Pet tab: UtilityBelt-style pet roster (2026-10-04)

RynthAi 0.6.22. Ships in the next installer after 2026.10.4.17.

## What changed

The ILT Hub **Pet** tab's flat "Carried essences" table (name / uses / order) is replaced
by a roster modelled on UtilityBelt's Pets tab.

### Three stat lines per pet

| Line | Contents |
|------|----------|
| 1 | Name, Bond, Lvl, Craft (coloured by damage type) |
| 2 | Sex (M/F, `n` neutered, `j` juvenile), growth, mutations, stored potency (active level and +damage %), mastery, damage type |
| 3 | Ratings `[D, DR, C, CD, CR, CDR]`, uses (red when empty), breeding status, "ID pending" until appraised |

Stats come from the ACECustom pet properties UtilityBelt reads (bond 9053, potency 9056,
mutations 9070–9075, maturity 9077, mastery 362, gear ratings 370–375 falling back to the
base rating ids). Active potency follows ACE: `min(stored, ceil(bond / 10))`, minimum 1,
capped at 150, +2% damage per level. Sex uses the server's GUID hash unless an override
bool is set.

Un-appraised essences are appraised automatically, two per 2-second scan. **Scan pack**
re-appraises every pet essence to refresh bond and potency after fights.

### Type and sort

- **Type**: Combat, Healing or Cosmetic. Combat essences and Healing Buddy / Dule box pets
  are classified automatically. Any essence can be re-typed: select it in your pack and press
  **Add sel.**, or right-click a row ("Move to …", "Clear type override"). Overrides are
  saved per character in `ilt-hub.json` (`Pet.Assignments`).
- **Sort**: Priority, Bond, Potency, Breed ready, Level, Uses, Name.

### Selecting and summoning by type

- **Combat**: the tick list is still the `ConsumableRules` Pet list that PetManager summons
  from, so there is no second summoner. With Sort = Priority, the arrows set the summon order.
- **Healing**: the tick picks the heal pet used by the healing helper. The old free-text
  "Heal-pet essence" field is replaced by this tick. No tick means the first Healing Buddy /
  Dule box, as before. The healing settings moved into a collapsible "Healing pet settings" header.
- **Cosmetic** (new): tick one display pet. **Keep cosmetic pet out** summons it in peace
  mode when no other pet is out and the combat summoner isn't hunting, re-summoning after a
  configurable delay (default 15 s).
- **Summon** uses the highlighted row, or the type's ticked pet. **Despawn** dismisses
  whatever pet is out by using the essence that summoned it again (matched by name, else the
  last essence summoned from the Hub). An **Active summon** line shows the pet that is out.

## Combat pet hand-off

`PetManager` has a new optional hook, `YieldPetForCombat`. When a pet already holds the slot
and monsters are near, PetManager asks the ILT Hub. If the pet is the cosmetic pet, the Hub
dismisses it, and PetManager waits 3 seconds and then summons a combat pet as usual. Other
pets (combat or heal) are never dismissed by this hook.

## Files

- New `IltHub/IltPetStats.cs`: stat model, property reader, 3-line formatter and sorter.
- `IltHub/IltPets.cs`: roster snapshot, classification, Summon/Despawn/Scan pack/Add sel.,
  cosmetic keep-out, new UI.
- `IltHub/IltHubState.cs`: `RosterKind`, `RosterSort`, `Assignments`, `CosmeticPetName`,
  `KeepCosmeticOut`, `CosmeticRespawnSeconds`, plus the new `IltPetAssignment` class. The values
  are normalized on load (`IltHubStore`).
- `Combat/PetManager.cs`: `YieldPetForCombat` hook. `IltHubController` wires it at login.
- Version 0.6.21 → 0.6.22.

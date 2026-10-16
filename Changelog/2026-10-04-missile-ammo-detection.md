# Missile combat: equipped ammo not detected (2026-10-04)

RynthAi 0.6.24.

## Symptom

Silentkelpie (InfiniteLeaftide) had ammo equipped for the Corrupted Bow, but RynthAi never
fired. The log shows the bow equipped and the stance reaching Missile (mode 4) with a target
locked, then `attack=never` on every combat tick from 22:13 onwards. Combat also never tried to
equip ammo from the pack.

## Cause

Combat refuses to fire in missile mode until `HasWieldedAmmo()` passes. That check had two weak
points:

1. **Name only.** A wielded stack only counted as ammo if its name contained "arrow" (bow),
   "quarrel"/"bolt" (crossbow) or "dart" (atlatl). Server-custom ammo with any other name was
   invisible, both in the ammo slot and in the pack, which is why combat never tried to equip
   anything either.
2. **Snapshot only.** It only looked at the direct-inventory snapshot. That snapshot merges in
   wielded gear the object cache had already filed as inventory with a readable wield slot.
   Ammo equipped before login can be missing from it, or read wield slot 0.

The log can't say which of the two hit Silentkelpie: no line names the ammo stack. The fix
covers both, and adds a diagnostic line (below) that names it next time.

## Fix

- **The ammo slot counts as ammo.** Any item the player wields in the ammunition slot (EquipMask
  `0x00800000`) is treated as ammo, whatever its name. This is safe because the server (ACE
  `Player_Inventory`) refuses to wield ammo whose AmmoType conflicts with the launcher.
- **Property-based recognition.** Loose ammo is also recognized by COMBAT_USE = Ammo (STypeInt 51),
  matched to the launcher by AMMO_TYPE (STypeInt 50) when readable. This covers:
  - auto-equipping from the pack;
  - Items > Missile ammunition > "Add selected as ammo";
  - the per-monster "Sel" ammo button.
- **Launcher detection.** Launcher kind now comes from the launcher's AMMO_TYPE when readable,
  else from its name. Items in the ammo slot are never mistaken for the launcher.
- **Fallback probe.** If the snapshot shows no ammo, combat probes every known missile-class or
  ammo-named object's wield slot through the host (wielder info, ownership info, then
  CurrentWieldedLocation). The result is reused for 750 ms.
- **`[AmmoDiag]` log line** (Combat category, at most every 15 s while ammo is missing). It names
  the ammo found outside the snapshot, or lists every wielded missile-class object with its slot,
  COMBAT_USE and AMMO_TYPE.
- `MissileCraftingManager` uses the same ammo-slot rule.

## Files

- `Combat/MissileAmmoHelper.cs`: slot constants, `IsAmmoSlot`, `KindFromAmmoType`,
  `IsWieldedAmmoForKind`, `GetAmmoKind`, `GetKindFromMissileWeapon`, public `PlayerWieldLocation`.
- `Combat/CombatManager.cs`: `HasWieldedAmmo` fallback probe and `[AmmoDiag]`.
  `TryEnsureMissileAmmoForCombat` reuses `HasWieldedAmmo`.
- `Combat/MissileCraftingManager.cs`: ammo-slot aware launcher and ammo checks.
- `LegacyUi/LegacyWeaponsUi.cs`, `LegacyUi/LegacyMonstersUi.cs`: property-based ammo recognition.
- Version 0.6.23 → 0.6.24.

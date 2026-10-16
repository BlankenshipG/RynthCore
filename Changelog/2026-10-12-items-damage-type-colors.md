# Items panel: weapons colored by damage type, as in UB-ILT

RynthCore engine, branch build-comp-10-11. It pairs with RynthAi 0.5.49, which colors the Pets
roster with the same palette.

## What changed

- `ImGui\Panels\DamageTypeColors.cs` (new): one text color per damage type, from UB-ILT's
  `LeaftideDamageTypeVtColors`, the palette its Items, Spell and Pets tabs share:
  - Slash 255,180,120. Pierce 240,200,140. Bludgeon 220,190,140.
  - Cold 160,210,255. Fire 255,130,70. Acid 160,255,140.
  - Lightning 255,240,120. Nether 200,160,255.
  - Anything else (Shield, Auto, None) keeps the normal text color.
  - RynthAi's `IltHub\IltDamageTypeTint.cs` holds the same values; keep the two tables in step.
- `ImGui\Panels\ItemsFace.cs`: each weapon's name and its Element button are drawn in that
  element's color. Changing the element recolors the row at once. Consumables are unchanged.

## Not tested in game yet

The engine builds cleanly. The colors haven't been checked in game yet.
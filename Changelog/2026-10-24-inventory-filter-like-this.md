# Inventory: filter like this, and find like this in Global Inventory

Two new lines in the Inventory panel's right-click menu, under "Add to loot profile...":

- **Filter inventory like this...** opens the Add to loot profile popup in a filter mode: the
  same Match choice (default "Items like this") and the same attribute checklist, but no
  profile, action or name. **Filter** asks RynthAi to judge every item you carry and wear with
  that rule (its looter's own evaluator), and the list shows only the matches.
  - A bar under the tools row names the filter (hover: its conditions), offers
    **Identify N** when some items can't be judged until identified, and clears it (X).
  - The panel asks RynthAi again every 2 s while it shows (find_refresh), so new loot and
    finished identifies show up; RynthAi only answers when the result changed.
  - Clearing filters, closing the Classic tools row or hiding the panel clears it. In Classic
    the bar is in the tools row, which opens when the filter is applied.
- **Find like this in Global Inventory...** (enabled when RynthInventory is loaded) uses the
  same popup; **Search** sends the rule to RynthInventory as `/ginv like <spec>`, which opens
  Global Inventory filtered to every character's matching items.

Needs the matching RynthSuite (RynthAi item_find / find_refresh / find_close, RynthInventory
0.1.5). An older RynthAi gets a "too old" note in the popup.

## Files

- `ImGui/Panels/LootAddDialog.cs`: `LootAddMode` (AddRule, FilterInventory, FilterGlobal),
  FilterBody, item_find and its answer (TakeFind), `NextSeq`.
- `ImGui/Panels/InventoryFace.LikeFilter.cs` (new): the filter state, refreshes and the bar.
- `ImGui/Panels/InventoryFace.cs`, `InventoryFace.Classic.cs`: menu lines, the list filter, the bar.
- `UI/Data/LootEditData.cs`: request Find / Scope / Candidates, draft Find, `LootEditItemFindDto`,
  state ItemFind (mirrors LootSdk LootEditWire).

## Follow-up: checklist filters and spell lists

- **The Inventory panel's filter button** (the list-check icon in the tools row) now opens the
  same checklist popup with no item: every attribute a loot rule can test, unticked, with a
  value to fill in (named values such as material, skill, slayer, class pick from a list).
  Filter judges your items the same way as "Filter inventory like this". The old rule list
  editor no longer hides items (its saved rules are left in the settings file).
- **Global Inventory** gets the same checklist ("Attribute filter +" next to the search box):
  one group open at a time, a ticked row shows its comparison and value; the search filters
  every character's saved items as you tick.
- **Spell lists** in every checklist (Add to loot profile, both filters, Global Inventory):
  - Add spell: Creature, Life or Item, a family from the spell table (buffs only: no debuffs,
    war or void), a level I to VIII; "or better" by default (exact / any tier in its row).
    Levels VII and VIII have their own names ("Might of the Lugians", "Incantation of Strength
    Self"), so the rule lists the family's spells by name; Self and Other both count.
  - Add cantrip: Minor, Major, Epic, Legendary or Paragon and a cantrip name.
  - Cantrip "or better" and the tier counts now include Paragon ("Paragon" or "Paragon's").
- Named values on an item's rows (material, wield skill...) can be changed from a list.

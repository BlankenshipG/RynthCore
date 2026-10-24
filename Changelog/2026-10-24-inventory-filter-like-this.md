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

# Add to loot profile: the attribute checklist (engine 2026.10.18.10)

The "Add to loot profile" popup can show RynthAi's attribute checklist (RynthAi 0.5.87). It
appears from the Inventory panel's right-click and from the Loot Editor's "Add selected item".

- **What you get:** every attribute the item has, grouped as Item, Weapon / Armor, Ratings, Spells, Pet essence, T11 and Requirements. Each row has a tick, a comparison (`>=`, `=`, `<=`, `not`; for spells "or better", "exact", "any tier") and a value box for numbers.
- **Choosing a comparison or typing a value** ticks the row. Each group header has "all" and "none".
- **Layout:** the list scrolls inside the popup, which is wider when the list shows (440 instead of 380).
- **What is sent:** only the rows you changed go to RynthAi with each preview; the rest keep RynthAi's defaults. The value box you're typing in isn't overwritten by a new preview.
- **An older RynthAi** answers without the checklist, and the popup shows its "Include T11 attributes" box as before.

Wire: `LootEditItemRequestDto.Choices`, `LootEditItemDraftDto.AttributesSupported` / `Attributes`
(`LootEditItemChoiceDto`, `LootEditItemAttributeDto`, `LootAttrOps`), mirroring RynthSuite's
`LootEditWire.cs`.

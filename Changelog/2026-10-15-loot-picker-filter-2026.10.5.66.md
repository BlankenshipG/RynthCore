# 2026.10.5.66 - Loot editor pickers: type-to-filter

Previous release: 2026.10.5.65.

## Change

The shared ImGui `FaceKit.Picker` (loot face key/value lists, and any other face
using it) shows a **Type to filter…** box when the list has 12 or more items.
Players can type part of a name (e.g. `olth` for Olthoi) instead of scrolling
long lists such as **Slayer Type** creature names, materials, or long keys.

Filter is case-insensitive substring match; empty filter shows the full list.
Selecting still returns the original list index to the caller.

## Pair with

RynthSuite loot vocab rename: long key 166 displays as **Slayer Type** with an
A–Z creature-type value table (see Suite changelog
`2026-10-15-loot-slayer-type-picker.md`).

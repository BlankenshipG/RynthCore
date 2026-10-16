# 2026.10.15.9 - Mini Remote: rail-off no longer orphans Crafting/sections

Previous: 2026.10.15.8.

## Fix

Undocked (and docked) Mini Remote could show only Crafting's Armor/Weapons/Jewelry
tabs with no Remote stats and no Gear/Games/Vaulted hub bar: the window was on a
non-Remote section with the sections rail hidden, so the hub tab bar never drew.

- Hiding the rail snaps back to Remote (restores miniremote-window tab bar).
- Picking a non-Remote section while the rail is off turns the rail back on.
- Draw safety: rail-off + non-Remote forces Remote.

## Files

- ImGui/Panels/MiniRemoteFace.cs
- ImGui/Panels/MiniRemoteFace.Sections.cs

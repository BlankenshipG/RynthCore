# 2026.10.16.10 - Chat standard filters: combat response breakdown

Previous: 2026.10.16.9.

## What

Standard filters (Chat Filters panel) can now hide combat responses by kind,
matching UB ChatHandler's melee / magic split plus the common prefixes:

- Melee / missile damage you deal or take (`… points of … damage`)
- Magic damage you deal or take (`… points with SpellName`)
- Recklessness / Sneak Attack / Critical prefixes (filter alone or with the rest)
- Periodic healing ticks (also under Combat, not only Items)
- Stamina / mana / health restore-or-spend lines
- Umbrella "All damage you deal / take" still covers both `of` and `with` forms
  (the old patterns only caught `of`, so war magic hits slipped through)

Missile hits share the melee text form in AC; environment misses stay on their
own filter.

## Files

- `UI/Panels/RynthChatPresets.cs`
- `UI/Data/ChatFilterPresets.cs`

## Deploy

`Publish-EngineCoreClr.ps1 -EngineOnly -AllowBehind -MsBuildArgs @('-p:Version=2026.10.16.10')`

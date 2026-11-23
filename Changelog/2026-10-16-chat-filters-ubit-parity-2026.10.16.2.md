# 2026.10.16.2 - Chat standard filters: UB-IT parity

Previous: 2026.10.16.1.

## What

Standard filters (`RynthChatPresets`, Chat Filters panel) now cover the rest of
UB-IT `ChatFilterTool` / classic `ChatHandler` options that can be matched from
line text alone:

- Kill-task **progress** (ACECustom / UB-IT)
- Master Arbitrator **spam** (selective) vs **all**
- Missile environment miss, periodic damage, fail-to-affect
- Give/take vitals, heal-self casts, healed-by-other, consumables
- Aetheria surge, cloak procs, banked luminance
- You're too busy / Casting status (when those lines hit chat)

Still not text-presets (need Decal WorldFilter / StatusTextIntercept): vendor
tells, monster tells, NPC chatter, StatusTextAll.

The "add as rule" picker (`ChatFilterPresets`) gained matching extras for the
same common spam lines.

## Files

- `UI/Panels/RynthChatPresets.cs`
- `UI/Data/ChatFilterPresets.cs`

## Deploy

`Publish-EngineCoreClr.ps1 -EngineOnly -AllowBehind -MsBuildArgs @('-p:Version=2026.10.16.2')`

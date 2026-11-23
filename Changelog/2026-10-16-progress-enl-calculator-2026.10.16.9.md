# 2026.10.16.9 - Skills Progression: Enl calculator UI

Previous: 2026.10.16.8.

## Change

Enlightenment on the Skills Progression tab now draws the ILT Enl calculator:
per-level cost rows (green while pack + character luminance cover the step),
still-need / have materials, and the 301–325 sigil craft chain. DTOs match
RynthILT's `AppendEnlCalculatorJson` (`levels`, `materials`, `chain`, ecoins).

Augmentations already show character AvailableLuminance (not banked) via ILT's
`banked` summary line; this build only wires the Enl planner UI.

## Files

- UI/Data/ProgressionData.cs
- ImGui/Panels/SkillsFace.Progression.cs

# Char panel rework: Progression on the Skills panel, Pets and Quests windows (RynthAi 0.5.4-legacy-ui)

Date: 2026-10-05
Branches (local): RynthCore `merge/aelrynth-2026.10.5.1`; RynthSuite `fix/rynthai-launcher-remote-commands`
Previous plugin version: 0.5.3-legacy-ui

## Why

The ILT Hub's Character tab mixed three different things:

- live session stats;
- character progression (XP, augmentations, enlightenment), which belongs with skills;
- quests.

Pets and quests are things you keep open while playing, so they get their own windows.

## Skills panel: new Progression tab (ILT worlds only)

The engine's Skills panel gains a third tab, **Progression**, next to Skills and Attributes &
Vitals. It shows only when RynthAi reports an ILT-like world. It has three sections:

- **XP planner**: XP for the next raise and for the next N raises (slider 1-100) of every attribute
  and vital. Costs are computed exactly from the client's XP tables (`SkillDat.CostOf`). The Hub's
  version used `/xp all` and +7.5-7.7% per-level estimates. Raising stays on the Attributes & Vitals
  tab.
- **Augmentations**: the Hub's planner with the same controls. It has Load /aug, Lum per Enlightened
  Coin, and per-aug targets. It shows luminance and coin totals, coins short and banked luminance.
- **Enlightenment**: same controls as the Hub had:
  - next step, plan-to-level totals, coins per token and tokens short;
  - the requirement checklist;
  - Enlighten now, auto-enlighten (arm / spend XP first / check interval).
  - "Enlighten now" and arming auto-enlighten ask in a Skills-panel confirmation first. The server
    still shows its own Yes/No dialog.

How it works:

- RynthAi still owns the planner math and the saved inputs, so the numbers can't drift from the
  Hub's.
- New export `RynthPluginGetProgressionJson` (built by `IltHubController.BuildProgressionJson` and
  `IltProgression.AppendSnapshotJson`). The engine polls it through the new
  `UiSources.Progression` (once a second while the Skills panel is open).
- Edits go back as the remote command `prog <sub> <args>`, handled by `IltProgression.HandleRemote`
  on the pump thread. The subcommands are `augload`, `augtarget`, `lumpercoin`, `enltarget`,
  `coinspertoken`, `enlighten`, `autoenl on|off|arm`, `spendfirst` and `checkevery`.

## Pets and Quests windows

- **Pets**: new undocked "Pets" window. Open it with `/ra pets [show|hide|toggle]`, the Pet tab's
  "Pop out" button, or Char right-click > Pets window. Closing it docks the roster back into the Pet
  tab. It draws only on ILT worlds. The open state is saved per character (`PetsWindowOpen`).
- **Quests**: the existing undocked quest window now carries Quest bonus (`/qb`) above the tracker.
  Open it with `/ra quests window [show|hide|toggle]` or Char right-click > Quests window.

## ILT Hub layout

- Tabs: **Character · Quests · Pet · Banking · Gear · Games**. Tab buttons are narrower (92 px) so
  all six fit the Hub's width.
- **Character**: Session rates, plus a note pointing to the Skills panel's Progression tab.
- **Quests** (new tab): Quest tracker + Quest bonus. It shows "open in its own window / Dock back"
  while the Quests window is open.
- **Pet**: the roster with a "Pop out" button. It shows "Dock back" while the Pets window is open.
- Saved tab selections still open the same tab. New tabs are appended to the persisted index list,
  and only the display order changed.
- Removed from the Hub: the XP calculator (replaced by the exact Skills-panel planner) and the Hub's
  copies of the Augmentations / Enlightenment UI. Unused `/xp all` capture and `/attr` raise code
  went with them. The spend-XP-before-auto-enlighten loop is unchanged.

## Dashboard

- **Char** launcher, left-click: ILT Hub (unchanged).
- **Char** launcher, right-click (new menu): ILT Hub, Pets window, Quests window, and
  Progression (Skills panel). Progression opens the Skills panel on the Progression tab
  (`SkillsFace.ShowProgression`).

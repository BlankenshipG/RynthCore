# Mini Remote is the ILT Hub (RynthAi 0.5.8-legacy-ui)

Date: 2026-10-05
Branches: RynthCore `merge/aelrynth-2026.10.5.1`, RynthSuite `fix/rynthai-launcher-remote-commands`

## What changed

The tabbed **ILT Hub** window is retired. The **Mini Remote** is now the Hub, the same way
UtilityBelt's ILT Mini Remote works.

### Mini Remote
- New top row with two buttons:
  - **V/H** switches between stacked sections (vertical) and three side-by-side columns
    (horizontal). The columns are: status (stats, target, pet, Reset XP / Report), actions
    (item slots, toggles) and economy (bank, rebuff, translate). The choice is saved
    (`HudState.MiniRemoteHorizontal`).
  - **Options** opens the same menu as right-clicking the remote.
- The Options menu has a new **ILT Hub** group at the top:
  - Character, Quests, Pets, Banking, Gear and Games each open or close their own window.
    An item is ticked while its window is open, and greyed out until the server reports the
    feature on.
  - A pointer to the Skills panel for Progression.
  - On non-ILT worlds: the world status, **Treat this world as ILT** and **Refresh server
    features**.
- The Options menu also has a **Horizontal layout** checkbox (same as V/H).
- The empty-bank hint now reads "Options > Banking".

### Hub section windows
- Each former tab is its own window. Each one opens near the centre the first time and is
  moved back on screen if its saved position leaves it off-screen.
  - **ILT Character**: world / server-options header (Refresh, Force ILT, Profiles), session
    rates, and the Progression note. This window also explains why the Hub is idle on other
    worlds.
  - **Quests**: quest bonus and quest tracker. The old "Pop out" button is gone, because this
    window is now the tracker's only home.
  - **Pets**, **Banking**, **Gear**, **Games**.
- Open/closed state is saved per character (`IltCharacterState.CharacterWindowOpen`,
  `BankingWindowOpen`, `GearWindowOpen`, `GamesWindowOpen`, plus the existing
  `PetsWindowOpen` and `QuestTrackerPoppedOut`).
- The Games HUD, the quest favorites HUD and the confirmation dialog are unchanged.
- Bank auto-refresh and the gear / split-arrow scans used to run only while the Hub window was
  open. They now run while the Mini Remote or any Hub section window is open.

### Commands
- `/ra hub` and `/ra hub show|hide|toggle` now show, hide or toggle the Mini Remote.
- New: `/ra hub open character|quests|pets|banking|gear|games [show|hide|toggle]`.
  Aliases: `char`, `rates`, `quest`, `pet`, `bank`, `game`.
- `/ra pets` and `/ra quests window` still work. They are the same as `/ra hub open pets` and
  `/ra hub open quests`.

### Engine dashboard (RynthAiFace)
- **Char** left-click opens the Mini Remote (the Hub).
- **Char** right-click menu: Mini Remote (ILT Hub), then Character, Quests, Pets, Banking,
  Gear and Games (each toggles its window), then Progression (Skills panel).

## Not ported from UtilityBelt's Mini Remote
- Storm and Reset Storm, the Guardian textbox, and the Fellow toggle.
- The LumS, KillL, PassL and Conv rate lines (RynthAi doesn't track these yet).
- Loot-profile and character-profile pickers on the remote.

## Versions
- RynthAi plugin: 0.5.8-legacy-ui (previous 0.5.7-legacy-ui).
- The next installer is 2026.10.5.8 (previous 2026.10.5.7).

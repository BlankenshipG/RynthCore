# Settings: On Death > Recall On Death

Engine 2026.10.5.35 (previous: 2026.10.5.34). Pairs with RynthAi 0.5.44-legacy-ui.

## What changed

- `UI/Data/SettingsData.cs`:
  - new properties `DeathRecallMode` (0 off, 1 spell, 2 item), `DeathRecallSpellId` (default 48,
    Primary Portal Recall) and `DeathRecallItem`;
  - a new "On Death" section after Safety Stops, with three rows:
    - "Recall On Death": Off, Cast a recall spell, or Use a recall item;
    - "Recall Spell": the nav editor's recall spells (`NavCommands.RecallIds`), shown only for
      the spell choice;
    - "Recall Item": the item name, shown only for the item choice.
- The spell names fill in from the client's spell table as it loads; until then they show as
  "Spell N (N)".
- What RynthAi does with it: after a death while the macro runs, once respawned, it casts the
  recall or uses the item, retries a fizzle, then rejoins the nav route and starts Travel Back To
  My Corpse from the landing spot.
- An older RynthAi ignores the new fields.

The engine builds in Release. Not yet tested in game.

## Installed

- Published to `C:\Games\RynthCore\Runtime` (file version 2026.10.5.35). Both running clients (pids 33684 and 61616) hot-reloaded to build 2026.10.5.35 cleanly. That was hot reload 3 of 3 for each, so the next engine update needs a client restart.
- Previous engine: `C:\Games\Rynthcore\PreviousReleases\2026.10.5.34-engine` and `C:\Games\_rynth-backups\20261011-pre-death-recall\Runtime`.

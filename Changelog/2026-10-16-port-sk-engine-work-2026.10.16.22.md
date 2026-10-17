# 2026.10.16.22 — sk-only engine work from 10-16 ported onto the test build

**Kind:** Hot push (Engine). Cherry-pick of sk `ae6de53` onto `test/hotbuild-2026-10-16`.

## Brought over

- **In-world hot-reload hold** (`ReloadInWorldGate` + `tools/ReloadInWorldGateTests`): a
  FileWatcher engine reload waits until logout / character select instead of tearing the engine
  down mid-play; a manual ERl / RL / `/rc rl` in-world is held the same way (it applies on
  logout). Needs loader 2.2 (`RynthCoreMayHotReload`; the installer builds it). `engine.json`
  `AllowInWorldAutoHotReload: true` restores the old behaviour. Tonight's engine hot-copy crash
  at 17:11 happened on a build without it.
- Progress tab Enlightenment calculator (`SkillsFace.Progression`, `ProgressionData`).
- Chat: combat standard filters and UBIT parity.
- Inventory: Item Count / GInv / RSort buttons, Mini Remote and GInv off the top bar.
- Loader/EntryPoint/settings changes the hold needs (`EngineSettings`, `Loader.c`).

## Kept from the test build

The Sense-under-RynthNav / Oracle companion strip bar layout and the Skills embed structure;
`SkillsFace.RequestTab` taken from sk (its chat command uses it).

## Checks

Engine builds; ReloadInWorldGateTests 14/14, EngineSettingsTests pass (213 assertions).

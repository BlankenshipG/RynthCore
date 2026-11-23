# 2026.10.5.56 - Diagnostics server list fills in; forcing a server loads its plugin

Previous release: 2026.10.5.55.

Goes with RynthSuite's `Changelog/2026-10-13-server-plugins-always-declare-0.1.4.md`:
the 16 server plugins at 0.1.4. Install them with this engine. No plugin API change
(still v86).

## Problem

- Options > Diagnostics said "No servers known yet" even in game. The server list
  (`SectionRegistry.Tick`) was only ticked from `ServerMenus.Tick`, which only ran
  inside `ImGuiBar.Draw`. With the bar hidden (the rail instead) nothing built the
  list, and no server plugin was sent its "servermenu" verdict.
- The load gate skips the server plugins for other hosts. Those servers never
  appeared in the list, so forcing one On did nothing: its plugin was not loaded.

## Change

- `EngineFrameController` now calls `ServerMenus.Tick()` every frame while panels
  are live, bar shown or not. `ImGuiBar.Draw` no longer calls it.
- `PluginManifestGate.LoadPlan.SkippedServerPlugins` records the server plugins
  the gate skipped. `PluginManager` remembers them
  (`PluginManager.ServerPlugins.cs`).
- New `NotLoadedServers` (pure): the skipped servers are added to the Diagnostics
  list as "installed but not loaded", and can be picked like any other.
- Forcing a not-loaded server On loads only that plugin, on the pump thread,
  without a rescan:
  - the picker in Options > Diagnostics,
  - `/rc server <id> on`,
  - a saved per-character override.
  The plugin is checked by the manifest resolver, loaded, initialized and sent
  `OnUIInitialized` / `OnLoginComplete`, then chat confirms
  "[RynthCore] Loaded X for Y (forced On in Options > Diagnostics)." A failure is
  reported in chat as well.
- The empty-list text now reads "No servers known yet (the list fills in once a
  character is in game)."

## Tests

- `tools/SectionsTests`: `NotLoadedServerList` (148/148 checks pass).
- `tools/PluginManifestTests`: `ServerGatePlanFromFiles` checks the skipped
  server plugin is recorded (236 assertions pass).
- RynthSuite `RynthServerPack.Tests`: 14 tests, 819 checks pass.

## Notes

- A forced plugin stays loaded for the session; setting the server back to Auto
  or Off takes effect on the next client start.

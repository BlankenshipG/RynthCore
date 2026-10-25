# 2026.10.5.53 - one server identity for every plugin, and a server picker

Previous release: 2026.10.5.52.

## Problem

Each plugin worked out "which server is this" on its own:

- RynthOracle used GetServerInfo (Aelrynth only), then the world name, then
  RynthILT's profile. It never saw the Mini Remote's per-character server
  overrides or a server plugin's declaration.
- ub-Rythai's ILT banking matched the world name only.
- Server plugins (RynthColdeve, RynthConquest, ...) knew their server's rules
  (botting policy, rule set, account limits, commands), but no other plugin
  could ask for them.
- The only way to force a server was "/rc server <id> on|off", one server at a
  time, with no place in Options to see or change it.

## Change

- Plugin API v85, `GetServerStateJson(byte* buffer, int capacity)`: the
  engine's server list as UTF-8 JSON (schema 1). It has `host`, `world`, `pick`
  ("auto", a server id, or "mixed"), `active`, `activeName` and `activePlugin`
  (the plugin that owns the active server, which other plugins ask for its
  settings). Each entry in `servers[]` has `id`, `name`, `active`, `detected`,
  `force`, `builtIn`, `plugin` and `reason`. The function returns the byte
  length; when that is >= capacity, nothing is written. SDK:
  `RynthCoreHost.HasGetServerStateJson` and `TryGetServerStateJson(out json)`.
- `ServerSelection` (pure): picking server X forces X On and every other server
  Off for this character; "auto" resets every override. It also answers which
  server is active (a forced-On server beats detection) and which plugin owns a
  server (the one that declares it, else the one with the most contributions
  for it, e.g. RynthILT for "ilt").
- Options > Diagnostics > Server: a picker listing "Auto (detect)" and every
  server from the loaded plugins, marked [active] or [detected]. Below it is a
  table showing each server's state, this character's override, and which
  plugin supplies its settings. A pick applies at once and reaches every plugin
  through the "servermenu" push and GetServerStateJson.
- Chat: `/rc server pick <id>|auto` does the same.
- Picks are logged at Info (`ServerSelection: ...`).

## Tests

- tools/SectionsTests: 116/116. New cases cover the pick plan, the current pick,
  the active server, the owner plugin, the JSON and the buffer convention.
- tools/PluginManifestTests: PASS (229 assertions). The API table layout now
  ends with GetServerStateJsonFn.

## Install

Installed locally on 2026-10-13 with RynthCore-Setup-2026.10.5.53.exe
(silent), along with RynthAi 0.5.61-legacy-ui, RynthOracle 0.2.4, ub-Rythai
3.1.12 and the 16 server plugins at 0.1.1 (RynthSuite ea84e27). Old DLLs were
backed up to each plugin's PreviousReleases folder.

The previously installed server plugins (copied at 19:06) were the managed
`bin` builds (about 35 KB), not the NativeAOT `publish` builds (about 2.3 MB),
so none of them could load. The ones installed now are the `publish` builds,
and each one exports RynthPluginQueryInterface.

Engine log categories in engine.json were set back to the defaults: Info for
General, D3D9, Compat, Plugin and UI; Render off; Verbose at Debug.

## Files

- `src/RynthCore.Engine/UI/Sections/ServerSelection.cs` (new)
- `src/RynthCore.Engine/UI/Sections/ServerStateJson.cs` (new)
- `src/RynthCore.Engine/UI/Sections/ServerSelectionStore.cs` (new)
- `src/RynthCore.Engine/ImGui/Panels/ServerPickSettingsUi.cs` (new)
- `src/RynthCore.Engine/ImGui/Panels/SettingsFace.cs`
- `src/RynthCore.Engine/UI/Data/SettingsData.cs`
- `src/RynthCore.Engine/UI/Panels/SettingsPanel.cs`
- `src/RynthCore.Engine/Compatibility/RynthCoreChatCommands.cs`
- `src/RynthCore.Engine/Plugins/PluginContract.cs`, `PluginContractVersion.cs`, `PluginManager.cs`
- `src/RynthCore.PluginSdk/RynthCoreApiNative.cs`, `RynthCoreHost.cs`, `build/RynthCore.PluginSdk.targets`
- `tools/SectionsTests/*`, `tools/PluginManifestTests/Program.cs`

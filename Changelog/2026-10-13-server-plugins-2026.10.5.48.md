# RynthCore 2026.10.5.48: one plugin per server, server detection, known-server picker

Branch `feat/dynamic-sections`, after 989d7a3 (2026.10.5.47). It pairs with the RynthSuite branch
of the same name, which adds 16 server plugins (RynthColdeve, RynthAelrynth, ...) and RynthILT
0.1.2; build them together.

| Component | Previous | Now |
|---|---|---|
| Engine | 2026.10.5.47 | 2026.10.5.48 |
| Plugin SDK manifest | no server block | optional `"server"` block |
| Engine plugin API | 81 | 81 (unchanged) |

## Server plugins: the right plugin for the server you connect to

Each server RynthSuite supports now has its own plugin, built like RynthILT, so each can be
updated on its own. The engine decides which one loads:

- **A plugin can name its server.** The SDK writes an optional `"server"` block into the embedded
  manifest: `{ id, name, hosts[], worldNames[] }`. Plugins set it in their csproj with
  `RynthPluginServerId`, `RynthPluginServerName`, `RynthPluginServerHosts` and
  `RynthPluginServerWorldNames` (lists separated by `|`). The generated `RynthPluginInfo` gets the
  same values as constants, so the plugin and the engine use one copy of the detection data.
  Older engines ignore the block.
- **Only the matching server plugin loads.** Before loading, the engine compares the client's
  connect host (the launch command line's `-h`) with every installed server plugin's hosts. When
  one plugin claims the host, the other server plugins are skipped and logged (not refused in chat).
  Hosts match exactly, as a host under a listed domain, or by a `*` pattern; an IP matches only
  itself.
- **It fails open.** When the host is unknown, or no server plugin claims it, every plugin loads as
  before. `engine.json` `"LoadAllServerPlugins": true` turns the check off.
- **The "servermenu" push goes to every server plugin** whose sections manifest declares a server,
  when it changes and every 10 s. So each plugin's server card shows the engine's verdict, the
  per-character override (Auto/On/Off) and the reason.
- **Plugin lookups prefer the exact name.** Commands and embedded panels matched plugin names by
  substring, so "RynthLeafDawn" could reach "RynthLeafdawning". An exact match now wins; the
  substring match remains the fallback.

## Launcher: set up an account on a known server

- The **Server Profile** dialog has a **Known server** picker with every server that has a server
  plugin (16 plus Infinite Leaftide). Picking one fills in Name, Host, Port and Emulator; Alias and
  Rodat stay as typed. The note under it names the plugin RynthCore loads for that server.
- Editing a saved profile selects its known server by host. Choosing "Custom server" leaves the
  fields alone, so any server still works.
- Hosts, ports and emulators come from the public server list
  (github.com/acresources/serverslist, checked 2026-10-13). Aelrynth is not on that list and uses
  aelrynth.com:9020.

## Publishing

- `installer/Publish-Update.ps1` builds and ships the 16 server plugins with the others. Each has a
  feed description, and the zip README lists them. RynthILT's description now calls it Infinite
  Leaftide's server plugin.

## Tests

- `tools/PluginManifestTests`: the server block's parsing, round trip and id rules; host matching;
  the load gate keeps only the host's plugin and fails open; a plan built from real files.
- `tools/LauncherSettingsTests`: the known-server catalog (unique ids and hosts, ApplyTo keeps the
  profile's id, alias and Rodat, host lookup including shared dynamic-DNS domains).
- RynthSuite `Tools/RynthServerPack.Tests` checks the catalog against the plugins: each entry names
  a real server plugin that claims its host, with the same port and emulator.

## Files

- `PluginSdk/Manifest/RynthPluginManifest.cs`: `RynthPluginServer`, parsing and writing the block.
- `PluginSdk/build/RynthCore.PluginSdk.targets`: the csproj properties, the manifest block and the
  `RynthPluginInfo` constants.
- `Engine/Plugins/PluginManifestGate.cs`: `SelectServerPlugins`, `LoadPlan.SkippedForServer`.
- `Engine/Plugins/PluginManager.cs`: passes the connect host to the gate; exact-name-first lookups.
- `Engine/Plugins/EngineSettings.cs`: `LoadAllServerPlugins`.
- `Engine/UI/ServerMenus.cs`: the push to every server plugin.
- `App/KnownServerCatalog.cs` (new), `App.Avalonia/ServerProfileDialog.axaml(.cs)`: the picker.
- `installer/Publish-Update.ps1`: the server plugins.

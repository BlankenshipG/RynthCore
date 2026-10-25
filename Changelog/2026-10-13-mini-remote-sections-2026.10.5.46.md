# RynthCore 2026.10.5.46: Mini Remote sections, server overrides per character

Branch `feat/dynamic-sections`, from 887ac12 (2026.10.5.45). It pairs with the RynthSuite branch of the
same name (RynthAi 0.5.55); build the two together. An older RynthAi still works: it publishes no
sections, so the engine shows its own panels and the remote as before.

| Component | Previous | Now |
|---|---|---|
| Engine | 2026.10.5.45 | 2026.10.5.46 |

The plugin contract version is unchanged. The new export is optional.

## The Mini Remote is a rail of sections

The Mini Remote window now has a narrow rail on its left. The first entry is the remote itself
(RynthAi's remote, or the engine's slot grid without RynthAi). Below it are the twelve core sections,
always in this order: Char, Map, Skills, Combat, Nav, Inventory, Crafting, Spells, Chat, Friends,
Fellowship, Pets. A plugin can add more sections; they show only while they have something in them.

- A section draws the engine panels that belong to it, whole, as tabs: Map has Radar and Dungeon Map,
  Skills has Skills, Combat has Damage and Monsters, Nav has Nav and RynthNav, Inventory has Inventory,
  Item Count and Items, Chat has Chat. Char starts with an overview.
- Below the panels is a server area: one collapsing group per active server ("Server: Infinite
  Leaftide"), then "Plugins" for items that apply on every world. Groups hold plugin option views and
  command buttons.
- Sections with no engine panel (Crafting, Spells, Friends, Fellowship, Pets) are filled by plugins.
  Without one they say so.
- The right-click menu lists the same sections and the server items, and can hide the rail.
- The chosen section and the rail setting are saved with the window.
- `/rc miniremote section <id>` opens a section. `/rc miniremote tab <tab>` opens the matching section
  when a plugin publishes it, else the remote's tab as before.

### Embedded panels

`ImGuiPanelHost.DrawEmbedded` draws a registered panel inside another window. Each host gets its own
panel instance, so the docked or floating panel keeps its own state. An embedded panel can't move or
resize the window it's drawn in. A copy that hasn't drawn for 750 ms gets `OnHidden`. A panel that
throws is shown as an error line instead of breaking the window. The Mini Remote can't embed itself.

## Plugin sections: `RynthPluginGetSectionsJson`

New optional export `const char* RynthPluginGetSectionsJson(void)` (UTF-8 JSON, schema 1). The engine
reads it every 2 seconds on the render thread, only after login.

- `servers`: servers the plugin knows. The built-in ids `ilt` and `aelrynth` keep the engine's own
  detection; a new id is detected from its `worldNames` and `hosts` lists.
- `sections`: extra sections, with a label and a rail order.
- `contributions`: what goes in a section. `server` empty means every world, else only while that
  server is active. `surface` is drawn inline through `RynthPluginRenderEmbed`; `command` is a button.
  `primary` makes a surface one of the section's tabs instead of an item in the server area.

Everything from a plugin is checked (`UI/Sections/SectionManifest.cs`):

- Ids are short lower-case words.
- Labels are one line, text is capped, a command must be a single `/...` chat command, and a surface
  name can't end in `:popout`.
- The lists are capped (16 servers, 32 sections, 128 contributions), and so are the JSON size
  (256 KB) and its depth.
- A bad entry is skipped and the rest of the manifest is kept.

## One server registry, with per-character overrides

`UI/Sections/SectionRegistry.cs` holds the servers (Infinite Leaftide and Aelrynth built in, plus any a
plugin declares), each with its detection, its override and the reason. It rebuilds the sections once a
second or when an override changes.

- Every server has an Auto / On / Off override per character. It is saved in
  `%LOCALAPPDATA%\RynthCore\server_force.txt`. Before login it holds for the session only.
- `/rc server` lists the servers. `/rc server <id> auto|on|off` sets one. The older
  `/rc server aelrynth|other|auto` still works.
- `/rc ilt on|off|auto` uses the same store.
- `/rc server names ilt [add|remove <pattern>]` edits engine.json `IltServerNames` (patterns up to 64
  characters; the defaults are copied in the first time).
- The Char overview shows each server's verdict, why, and Auto / On / Off buttons.
- The engine's own server items (Guardian, Progression, Bounties, and RynthAi's tabs when RynthAi
  publishes no sections) are now entries in the registry.

The "servermenu" push to RynthAi now also carries the servers (`V id name active force detected`) and
the rail (`X id label`). An older RynthAi skips those lines.

## Tests

- New `tools/SectionsTests` (77 checks): the overrides file, the manifest parser and its limits, the
  section catalog (core order, server-scoped items, plugin sections) and declared-server detection.
- `tools/EngineSettingsTests` checks `EditIltServerName` against a real engine.json: the defaults
  are kept on the first edit, the launcher's keys survive, and repeats, unknown, empty and overlong
  patterns change nothing. It now links `IltDetect.cs` and `AelrynthWire.cs`.
- RynthSuite's host tests link the parser and catalog and check RynthAi's manifest against them.
- Every other test project passes, apart from two known failures that already happen on 2026.10.5.45:
  RynthLua.ScriptWindowsTests (4 checks) and the RynthAi host test that needs the real dat files.

## Files

- New: `UI/Sections/ServerForce.cs`, `ServerForceStore.cs`, `SectionManifest.cs`, `SectionCatalog.cs`,
  `SectionRegistry.cs`; `ImGui/Panels/MiniRemoteFace.Sections.cs`; `tools/SectionsTests`.
- Tests changed: `tools/EngineSettingsTests`.
- Changed: `ImGui/ImGuiPanelHost.cs`, `ImGui/Panels/MiniRemoteFace.cs`, `UI/ServerMenus.cs`,
  `Compatibility/IltInfo.cs`, `Compatibility/ServerInfo.cs`, `Compatibility/RynthCoreChatCommands.cs`,
  `Plugins/EngineSettings.cs`, `Plugins/LoadedPlugin.cs`, `Plugins/PluginLoader.cs`,
  `Plugins/PluginManager.cs`, `EntryPoint.cs`.
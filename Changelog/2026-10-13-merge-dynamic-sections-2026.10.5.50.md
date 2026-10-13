# RynthCore 2026.10.5.50: merge feat/dynamic-sections into build-comp-10-11

This merge brings the `feat/dynamic-sections` branch onto the local build line. It pairs with the
RynthSuite merge of the same name, so build the two together.

| Component | Previous | Now |
|---|---|---|
| Engine | 2026.10.5.49 | 2026.10.5.50 |
| Engine plugin API | 81 | 81 (unchanged) |

The branch's own notes (`2026-10-13-mini-remote-sections-2026.10.5.46.md`,
`...-fellowship-menu-ilt-settings-push-2026.10.5.47.md` and
`...-server-plugins-2026.10.5.48.md`) are kept as they were written. Their version numbers come
from the branch and were never shipped on this line. Everything they describe ships as
2026.10.5.50.

## What arrives

- **Mini Remote sections.** These come from `SectionRegistry`, `SectionCatalog`,
  `SectionManifest` and `MiniRemoteFace.Sections`. Plugins declare their sections, and the engine
  places them into the Mini Remote menus by server.
- **Per-character server overrides.** `ServerForce`/`ServerForceStore` save Auto, On or Off for
  each character and server. Set them with `/rc server <id> auto|on|off` and
  `/rc server names ilt add|remove <pattern>`. The "servermenu" push (lines V, X, S, I and N)
  reaches RynthAi, RynthILT and every server plugin.
- **One plugin per server.** This is `PluginManifestGate.SelectServerPlugins` together with the
  SDK's `"server"` manifest block. It fails open, and `engine.json`
  `"LoadAllServerPlugins": true` turns it off.
- **Known-server picker** in the launcher's Server Profile dialog.

## Conflict decisions

- `IltInfo` keeps this line's detection, combined with RynthILT's reported verdict when the
  override is Auto. RynthILT now reports only its local verdict, so the engine's verdict cannot
  echo back and latch ILT on.
- `EntryPoint.RegisterServerMenus` uses `SectionRegistry`. The Char menu keeps this line's
  Quests and Quest Timers entries. Its Fellowship item runs `/ro fellowship`, which now opens
  RynthOracle's Fellowship tab.
- `RynthAiFace` and its drawers keep this line's Hub-free layout and add the section hooks.
- `ServerProfileDialog` keeps the known-server picker.
- `installer/Publish-Update.ps1` keeps this line's plugin list and adds the 16 server plugins.
- `EngineSettings` adds `LoadAllServerPlugins` alongside this line's settings.

## Deployment note

The 16 server plugins stay disabled in the launcher until this engine is installed. An older
engine has no server gate, so it would load all of them at once.

## Build and install

- `installer\Output\RynthCore-Setup-2026.10.5.50.exe` and `RynthCore-2026.10.5.50-deploy.zip`
  (126 MB, verified). 2026.10.5.49 is recorded as the previous release.
- Installed silently on this machine. RynthOracle, RynthILT and the 16 server plugins aren't in
  the installer and were copied in directly. Each replaced plugin DLL is saved in that plugin's
  `PreviousReleases\<version>` folder.
- The launcher's RynthLua entry now points at `C:\Games\RynthSuite\RynthLua`. The old settings
  file is saved as `appsettings.json.pre-2026.10.5.50.bak`. The server plugins stay enabled,
  because this engine has the server gate.

## Tests

- `tools/EngineSettingsTests`: 207 passed.
- `tools/PluginManifestTests`: 227 passed.
- `tools/SectionsTests`: 77 passed.
- `tools/LauncherSettingsTests`: 129 passed.

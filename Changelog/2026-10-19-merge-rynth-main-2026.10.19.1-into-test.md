# Merge rynth main (2026.10.18.1 and 2026.10.19.1 snapshots) into the test branch (2026-10-19)

Brings rynth's two newest release snapshots (aelrynth main `605ec6f` and `44074df`) into
`test/hotbuild-2026-10-16`, by way of `test/hotbuild-2026-10-16-on-aelrynth`. The owner asked for a
careful merge, keeping both sides' changes. The plain merge had 38 conflicting files (139 sections).

## How it was merged

- **Files rynth built on our published `sk` code (85% or more of it adopted).** These were merged
  against `upstream/sk` as the base, so his edits land on top of ours. Eight files.
- **Every other file.** Resolved section by section against the true merge base.

## Taken from rynth

- **Plugin contract v87/v88.** `GetWieldedItems` (EquipmentTracker); managed plugins find their
  shared dependencies in `Runtime\PluginShared`.
- **Settings and Skills.**
  - `SkillsRouting`: the Skills bar icon shows only while Oracle can't host the Skills tabs.
  - The Oracle Skills embed lifecycle (`OracleSkillsEmbed.Bind`, `NoteDrawn` and `Sweep`).
  - The hardened Enl calculator data (lenient row converters, preformatted row text) and its
    drawing ("Show all", narrow-panel scaling).
- **Chat.** `ChatRetailVerdict`, `SharedFileWatch` for the settings file, and his coordinate-link
  helpers in `ChatCoords`. The standard filters "Damage you deal / take" keep their original meaning.
- **Inventory.** Rynth's header layout for the related-panel buttons: icons, or a "more" menu when
  short. The Vault is never dropped. This applies to both looks.
- **Reload hold.** `HoldReloadInWorld` (opt-in, default off) and `HoldReloadInWorldMaxMinutes`,
  his reworked `ReloadInWorldGate`, and its tests.
- **Display.** `RememberAcWindowPosition`: game-window placement split from panel layout, opt-in.
  It is shown in Settings > Display as "Remember Game Window Position".
- **Release pipeline.** `Publish-Update.ps1` builds the plugins managed by default (collectible, so
  a reload frees them), collects `PluginShared`, and passes `-PluginSharedDir` and `-RynthAiDll`
  to the installer.

## Kept from the test branch

- **`AcWindowChrome`.**
  - Ours is kept: Fills Monitor, Reset Window Borders, Save Window Position, a separate borderless
    rect, and the Graphics profiles.
  - Its placement save and restore now follow `RememberAcWindowPosition`.
  - Rynth's threading rework (window changes posted to the game WndProc, `AcWindowChromePolicy`)
    is not ported yet. Follow-up.
- **`BorderlessFillMonitor`.** Added back to rynth's `EngineSettings`.
- **The loader gate.** `RynthCoreMayHotReload` and `RequestManualReload` are kept, now under
  `HoldReloadInWorld` in place of the removed `AllowInWorldAutoHotReload`. With the hold on, the
  loader waits in the world for every reload, a hand reload too (a mid-session CoreCLR re-init can
  still crash). With it off (the default), reloads go ahead mid-session, as upstream does.
- **ImGui bar.** Sense stays under RynthNav and Item Count / Mini Remote under Inventory. Skills
  uses rynth's conditional icon. The ILT bar section stays removed.
- **Installer.** Our full-installer `Build-Installer.ps1`, with rynth's managed-RynthAi and
  `PluginShared` staging ported into it. Our optional Decal bridge, versioned installer copy, the
  `RynthLevistras` server plugin, and server plugins in the zip.
- **Automated chat to System.** `AutomatedToSystem`; the Enl calculator's "How to get" column and
  vendor-rate editors; the Augmentations STILL NEEDED row.
- **Items.** Weapon damage-type tints are combined with rynth's move up / down buttons.

## Tests

- **All 59 `tools/*Tests` and benches pass.**
- **`PluginManifestTests`.** Our "v86 slot last" opening was dropped; rynth's slot counts include
  his v87 slot.
- **`EngineSettingsTests`.** No longer prints the removed `AllowInWorldAutoHotReload`.

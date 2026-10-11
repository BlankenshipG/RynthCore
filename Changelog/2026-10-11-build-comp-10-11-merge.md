# Local build branch build-comp-10-11: rynth release 2026.10.11.1 plus local work

**Date:** 2026-10-11
**Branch:** `build-comp-10-11` (local only, not pushed)

## Contents

1. `main` merged with rynth's `aelrynth/main` (release 2026.10.11.1), commit `f71981b`.
2. Inventory multi-select (`feat/inventory-multi-select`, see
   `2026-10-11-inventory-multi-select.md`), commit `63db698`. A group drop also keeps rynth's
   in-pack slot (`DropOnSelection(..., slot)`).
3. Follow-up fixes (this commit).

## How the merge was settled

- **Rynth's side:**
  - Discord relay (`DiscordRouteQueue`, `tools/ChatDiscordTests`);
  - installer, injector, loader and launcher diagnostics;
  - log paths, plugin path dedupe, layered windows;
  - chat line colours;
  - `EngineSettingsFile.Merge`, which keeps the launcher's keys. Our `EnablePluginOverlayWindows`
    and `LoggingLevel` are added to `OwnedKeys`.
- **Shared install paths:** every project links the read-only `src/Shared/RynthInstallPaths.cs`.
  Our launcher-only helpers (`ReadSetting`, `DeleteUserSetting`) moved to
  `RynthCore.App.Avalonia/InstallerHandoff.cs`.
- **Both:**
  - frame controller (keeps plugin overlay windows live);
  - panel spec (`DefaultPos`, `OpenCentered`, `NoResize`);
  - Win32 backend (`CanRunOnGameThread`);
  - chat presets DTO;
  - heartbeat logging.

## Follow-up fixes

- Settings > RynthAi: one item info toggle. Rynth's second "Item Info On Select" row under
  Inventory Management bound the same setting as our "Describe Items When Selected" row in the
  Item Info section. The Item Info section stays, with the "Item Info Options" button, and its
  tooltip now says the bot's own picks are skipped.
- `RynthCore.Engine.csproj`: the `Tmds.DBus.Protocol` 0.21.3 pin was listed twice (both sides
  added it). One remains.

## Verification

- Engine, App.Avalonia, Injector, Loader and StatusAgent build with 0 errors.
- Tests pass:
  - EngineSettingsTests (164)
  - ChatDiscordTests (316)
  - ChatFilterTests (823)
  - InstallPathsTests (56)
  - PanelWindowTests
  - PluginManifestTests

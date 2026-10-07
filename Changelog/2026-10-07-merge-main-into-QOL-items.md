# QOL-items: main merged in, PR #10 conflicts resolved (plugin API 80)

**Date:** 2026-10-07
**Branch:** `QOL-items` ([PR #10](https://github.com/BlankenshipG/RynthCore/pull/10), into `main`)
**Merged:** `origin/main` 784ddf1 (engine 2026.10.5.16) into `QOL-items` f2bd3b2
**Plugin API:** 80 (previous: 77 on `QOL-items`, 79 on `main`)

## Why

PR #10 could not merge: `main` took the 2026-10-05 evening upstream snapshot (913a2a0) and the
three commits that sent the SK features upstream (9709a39, 4be686e, 143eaa9), and both lines
edited the same 15 files. This merge brings `main` into `QOL-items` so the PR merges cleanly.
Nothing on `main` is rewritten.

## Plugin API: GetMergeStackResult moves to v80

Both lines used API v77 for different table fields:
- `QOL-items`: v77 = `GetMergeStackResultFn`.
- `main` (upstream): v77 = `GetScreenModeFn`, `GetUiHookFlagsFn`; v78 =
  `SetServerMessageInterestFn`; v79 = `GetTrainingInfoFn`, `RaiseFn`, `TrainSkillFn`.

`main`'s numbering is the one upstream ships, so it stays. `GetMergeStackResultFn` is appended
after `TrainSkillFn` as v80, in both the engine's `RynthCoreAPI` and the SDK's
`RynthCoreApiNative`. `PluginContractVersion.Current`, `RynthCoreSdkApiVersion` (SDK targets) and
`RynthCoreHost.HasGetMergeStackResult` (now `Version >= 80`) follow.

A plugin built against the old `QOL-items` SDK would read the wrong slot for
`GetMergeStackResult`. No RynthSuite plugin calls it, and RynthAi (RynthSuite `main` 272e9f5)
builds against the merged SDK, but rebuild plugins against this SDK before running them on this
engine.

## Conflicts resolved

`main`'s side already contains the `QOL-items` change plus upstream's additions in most files, so
those hunks take `main`:
- `ImGui/EngineFrameController.cs`: line endings only; `main` adds the Client::Cleanup guard.
- `ImGui/Panels/NavFace.cs`: header lists the route editor.
- `Plugins/EngineSettings.cs`: `DisabledUiHooks` read before `LoggingLevel`.
- `Plugins/LoadedPlugin.cs`: v77/v78 export pointers, `Manifest`, `NotStartedReason`.
- `RynthLog.cs`: `Heartbeat` (always written), the Move / UseDone traces, `Write` wrapper.
- `UI/Data/ChatData.cs`, `UI/Data/NavData.cs`: route editor fields and requests.
- `UI/Data/SettingsData.cs`, `ImGui/Panels/SettingsFace.cs`, `UI/Panels/SettingsPanel.cs`: the
  TraceCategories row next to Button / Text / Status; the Diagnostics tab icon.
- `StatusAgent/AgentConfig.cs`: `CellDatPath` and `NavAtlasPath`.

Merged by hand:
- `Plugins/PluginContract.cs`: header lists both `RynthPluginRenderOverlay` and
  `RynthPluginOnServerMessage`; table as above; `PluginContractVersion` lives only in
  `PluginContractVersion.cs`.
- `Plugins/PluginManager.cs`: `main` gathers the plugin list first and loads it afterwards, so
  `QOL-items`' duplicate guard (same file name or same resolved path) now records each candidate
  as it is accepted instead of after loading.
- `ImGui/Panels/SettingsFace.cs`: git kept two identical copies of `TextRow`, `StatusRow` and
  `ButtonRow` (both lines added them); one copy removed.
- `tools/PluginManifestTests`: the table's tail is now the v79 slots then `GetMergeStackResultFn`.

## Verification

- `RynthCore.sln` (Release, x86): builds, no errors.
- PluginManifestTests 156/156, LauncherUpdateTests 134/134, TrainingCostTests 70/70,
  ReloadDeferralTests 19/19, NetMessageTests 203/203.
- StatusAgentTests does not compile, the same as on `main` (its project compiles
  `AgentConfig.cs` without `RynthInstallPaths`); not caused by this merge.
- RynthSuite `main` against this SDK: RynthAi builds; host tests 431 passed, 5 known failures,
  1 unrelated failure (map bake reads the real dats), the same as before.
- No release was built or deployed from this merge.

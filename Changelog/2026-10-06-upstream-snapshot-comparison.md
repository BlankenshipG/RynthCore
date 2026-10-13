# Upstream snapshot comparison: rynth/RynthCore 2026.10.6.1 vs GitHub main

Date: 2026-10-06
Report only. Nothing has been merged or pushed.

| Side | Ref | Commit |
|---|---|---|
| Upstream (aelrynth `rynth/RynthCore`) | `aelrynth/main` | `abf9146` Snapshot for release 2026.10.6.1 (internal 1cbe514) |
| GitHub (`BlankenshipG/RynthCore`) | `origin/main` | `143eaa9` feat(ui): nav route editing, progression, charms and chat presets |
| Common base | merge-base | `913a2a0` release 2026.10.5.20 |

Upstream snapshot not on GitHub: `abf9146`, 93 files, +10168 / -597.

GitHub commits not upstream. All three are in open rynth/RynthCore PR #2; PR #1 is superseded.
- `9709a39` install-aware paths, logging levels, item drag bridge
- `4be686e` plugin overlay windows and create-time identity seeds
- `143eaa9` nav route editing, progression, charms and chat presets

## Policy for the next merge

- **Keep everything we sent up:**
  - logging levels and desktop log;
  - item drag bridge;
  - plugin overlay windows;
  - progression, charms and chat presets;
  - nav route editing;
  - install-aware paths.
- **Bring down the new upstream items flagged below**, merge them in and test them.

## FLAG: new upstream items to bring down, merge and test

### New files (38): missing on GitHub
- **Flight recorder** (network capture ring):
  - `Net/FlightCapture.cs`, `Net/FlightFormat.cs`, `Net/FlightRecorder.cs`, `Net/FlightRing.cs`
  - `tools/FlightRecorderTests`
- **Video patch:**
  - `D3D9/VideoPatch.cs`, `D3D9/VideoPatchPolicy.cs`, `ImGui/VideoPatchBanner.cs`
  - `tools/VideoPatchTests`
- **Meta editor:**
  - `ImGui/Panels/MetaFace.Editor.cs`
  - `UI/Data/MetaEditing.cs`, `UI/Data/MetaLocation.cs`, `UI/Data/MetaRuleDto.cs`
  - `tools/MetaEditTests`
- **Item count HUD:**
  - `ImGui/Panels/ItemCountFace.cs`
  - `UI/Data/ItemHudModel.cs`, `UI/Data/ItemHudStore.cs`
  - `tools/ItemHudTests`
- **Mini remote:** `ImGui/Panels/MiniRemoteFace.cs`.
- **Engine settings:**
  - `Plugins/EngineSettingsFile.cs`, `Plugins/PluginPathDedupe.cs`
  - `tools/EngineSettingsTests`
- **Chat filters:**
  - `UI/Data/ChatFilterPresets.cs`, `UI/Data/ChatRuleMatching.cs`, `UI/Data/ChatSettingsDto.cs`
  - `tools/ChatFilterTests`
- **Panels:** `UI/PanelDestroyFallback.cs`, `tools/PanelWindowTests`.
- **Install paths:** `src/Shared/RynthInstallPaths.cs`, `tools/InstallPathsTests`. See the
  overlaps below.

### Upstream edits to files GitHub never changed (31): merge cleanly
- **Launcher:** `RynthUpdater.NavData`, `UsageStats`.
- **Compatibility:**
  - `AcActionTrace`, `AcMainThreadQueue`, `BusyCountHooks`, `ClientActionGates`
  - `GameTickHooks`, `HeartbeatLogger`, `InventoryModel`, `LoginLifecycleHooks`
  - `RawPacketHooks`, `RynthCoreChatCommands`, `SmartBoxHooks`
- **D3D9:** `AcUiPassHook`, `Nav3DRenderInjector`.
- **ImGui:**
  - `ImGuiBar`, `ImGuiPopOuts`, `MonsterHud`
  - `MetaFace`, `MetaFace.Schedule`, `P1Faces`, `RynthNavFace`
  - `UiDropTargets`, `RynthNavData`
- **Tools:** test project files, `PluginManifestTests/Program.cs`, `tools/pe_pattern.py`.

### Edited on both sides (24): needs a hand merge (14 conflict in a trial merge)

Conflicting:
- `RynthCore.App.Avalonia/LauncherDiag.cs`, `MainWindow.axaml.cs`, `RynthCore.App.Avalonia.csproj`
- `Engine/ImGui/EngineFrameController.cs`
- `Engine/ImGui/Panels/ChatFiltersFace.cs`, `Engine/ImGui/Panels/InventoryFace.cs`
- `Engine/LogPaths.cs`, `Engine/RynthCore.Engine.csproj`
- `Engine/UI/Data/ChatData.cs`, `Engine/UI/Data/MetaData.cs`
- `Injector/Program.cs`, `Loader/EntryPoint.cs`
- `StatusAgent/AgentConfig.cs`, `StatusAgent/RynthCore.StatusAgent.csproj`

Auto-merged, but changed on both sides, so review them:
- `D3D9/EndSceneHook.cs`, `EngineLifecycle.cs`, `EntryPoint.cs`
- `ImGui/Win32Backend.cs`
- `Plugins/EngineSettings.cs`, `Plugins/PluginManager.cs`
- `UI/Data/SettingsData.cs`, `UI/LayeredWindow.cs`
- `Injector.csproj`, `Loader.csproj`

## Overlaps to resolve when merging

1. **Install paths: two copies of `RynthInstallPaths`.** GitHub has
   `src/RynthCore.App/RynthInstallPaths.cs`; upstream has `src/Shared/RynthInstallPaths.cs`,
   linked into several projects. The two differ by about 190 lines. Keep one and update the
   links (App, Engine, Injector, Loader, StatusAgent, tests).
2. **Chat presets:** our `UI/Panels/RynthChatPresets.cs` against upstream's
   `ChatFilterPresets` / `ChatRuleMatching` / `ChatSettingsDto`, which touch the same
   `ChatData.cs` and `ChatFiltersFace.cs`. Review both side by side; don't keep two preset
   systems.
3. **Panel destroy:** our posted-destroy fix (PR #2) against upstream's
   `PanelDestroyFallback.cs`, both in `LayeredWindow.cs`.
4. **Logging:** our `LogSettings.cs` and logging levels against upstream's `LogPaths.cs` and
   `EngineSettingsFile.cs` changes.

## Suggested test pass after merging

- New upstream tools: `ChatFilterTests`, `EngineSettingsTests`, `FlightRecorderTests`,
  `InstallPathsTests`, `ItemHudTests`, `MetaEditTests`, `PanelWindowTests`, `VideoPatchTests`.
- Existing tools: `PluginManifestTests`, `StatusAgentTests`, `LauncherUpdateTests`,
  `NavDataUpdateTests`, `UsageStatsTests`.
- NativeAOT Engine publish.
- In game:
  - overlay windows and item drag;
  - chat presets and filters;
  - meta editor;
  - item count HUD;
  - video patch banner;
  - logging level settings in the launcher.

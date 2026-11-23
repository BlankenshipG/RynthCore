# 2026.10.5.59 - Loot Timers show their optimized defaults, with a button to use them

Previous release: 2026.10.5.58 (installed engine 2026.10.5.57).

Goes with RynthSuite's `Changelog/2026-10-14-loot-baseline-0.5.65.md` (RynthAi 0.5.65), which
measured the loot timer baseline and applied it to RynthAi's own Advanced Settings. The
engine's Settings panel (Looting tab) still showed the old list with no defaults.

## Change

- **Settings > Looting > Loot Timers (ms) - (optimized default).** Each timer's label ends
  with its optimized default in brackets:
  - Content Settle (100)
  - Empty Corpse Wait (300)
  - Assess Window (300)
  - Loot Retry Timeout (1000)
  - Corpse Open Retry (1500)
  - Corpse Timeout (12000)
- **Use optimized defaults** button: sets all six timers to those values and saves them to
  RynthAi like a typed value. It works in the in-game panel and in the launcher's panel.
- Each timer has a tooltip (what it waits for, and the measured appraisal times for Assess
  Window).
- Inter-Item Delay and Closing Delay are no longer shown: RynthAi's corpse looting never reads
  them (RynthAi hid them in 0.5.65). They are still carried so a save doesn't send them missing.
- The engine's own defaults for the timers (used until RynthAi sends its settings) now match
  the optimized defaults: Assess Window 200 -> 300 ms, Loot Retry Timeout 500 -> 1000 ms.

## Code

- `UI/Data/SettingsData.cs`: new `LootTimerDefaults` (the six values, same as RynthSuite's
  `LootBaseline.cs`; `Label`, `Apply`, `AllDefault`). `SettingRow` gains an optional
  `Edit` action for buttons that change settings, built with `EditButton`.
- `ImGui/Panels/SettingsFace.cs` and `UI/Panels/SettingsPanel.cs`: a button row with `Edit`
  applies it to the panel's copy and saves it (the launcher panel also rebuilds the tab so the
  number boxes show the new values).

## Tests

- `dotnet build src\RynthCore.Engine\RynthCore.Engine.csproj -c Release`: 0 errors. No test
  project compiles `SettingsData.cs`.

## Install

Installed locally on 2026-10-14: `RynthCore.Engine.dll` and `.pdb` 2026.10.5.59 (RynthCore 1590c2a,
published with `scripts\Publish-EngineCoreClr.ps1 -EngineOnly -AllowBehind -MsBuildArgs -p:Version=2026.10.5.59`;
the script's `status-export` branch no longer exists, and the installed 2026.10.5.57 commit is in
this build) copied to C:\Games\RynthCore\Runtime. The 2026.10.5.57 engine was backed up to
PreviousReleases\2026.10.5.57-engine. Running clients hot-reload onto the new engine.

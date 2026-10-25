# 2026.10.16.5 - Hot reload: ERl no longer bypasses the in-world hold

Previous: 2026.10.16.4.

## Why

Automatic FileWatcher hold (16.3) worked — clients logged
`holding reload - engine says not safe yet (in-world)`. Pressing ERl / RL /
`/rc rl` while still in-world set the manual bypass, `MayHotReload` returned 1,
Shutdown skipped the world gate, and CoreCLR re-init AVed (e.g. pid 37684 at
10:25:42, `clr_37684_*.dmp`, gen 2 init then native crash in coreclr).

## What

- `RynthCoreMayHotReload` no longer treats a recent ERl as "safe now".
- `ReloadInWorldGate.ShouldHold` holds for manual as well as automatic reloads
  (unless `AllowInWorldAutoHotReload`).
- Shutdown's world gate always runs while in-world; ERl only skips the combat /
  plugin blocker wait once out of the world.
- ERl / RL / `/rc rl` / hotkey go through `RequestManualReload`: signal still
  fires (applies on logout), chat warns once while in-world.

## Deploy

Logout / character select once so clients on 16.3–16.4 pick this up (do not ERl
in-world on those builds). Then:

`Publish-EngineCoreClr.ps1 -AllowBehind -MsBuildArgs @('-p:Version=2026.10.16.5')`

## Files

- `ReloadInWorldGate.cs`, `EntryPoint.cs`, `EngineSettings.cs`
- `RynthCoreShell.cs`, `ImGuiBar.cs`, `HotkeyService.cs`, `RynthCoreChatCommands.cs`
- `AvaloniaOverlay.cs`, `DcompOverlayWindow.cs`, `Loader.c`
- `tools/ReloadInWorldGateTests`

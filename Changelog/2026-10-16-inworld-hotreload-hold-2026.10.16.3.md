# 2026.10.16.3 - Hold automatic hot reloads while in-world

Previous: 2026.10.16.2.

## Why

Stacked FileWatcher hot publishes while logged in were crashing clients: the
loader shut the engine down, then CoreCLR re-init failed mid-flight (no
`Reload: complete`, large CLR dumps). Combat deferral only waits for plugin
blockers; a quiet in-world unload still tore everything down.

## What

- Automatic FileWatcher reloads wait until logout / character select (or ERl /
  `/rc rl`). No force-timeout while still in the world.
- New engine export `RynthCoreMayHotReload` (loader 2.2+ polls before Shutdown).
- Old loaders still safe: `RynthCoreShutdown` runs the same in-world gate.
- `engine.json` `AllowInWorldAutoHotReload` (default false) restores the old
  mid-session auto-reload behaviour if set true by hand.

## Files

- `ReloadInWorldGate.cs`, `EntryPoint.cs`, `Plugins/EngineSettings.cs`
- `native/Loader/Loader.c`, `Loader.h` (LOADER_VERSION 2.2)
- `tools/ReloadInWorldGateTests`

## Deploy

`Publish-EngineCoreClr.ps1 -AllowBehind -MsBuildArgs @('-p:Version=2026.10.16.3')`

EngineOnly is enough for the Shutdown gate on running clients. Full publish
stages loader 2.2; running clients keep the old loader until restart (engine
gate still holds).

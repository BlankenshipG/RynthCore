# RynthCore Engine v57 — plugin extra path dedupe and unload cleanup

**Build stamp:** `2026-04-22-v57-plugin-extra-path-dedupe`

## Problem

When the same logical DLL appeared twice — typically `RynthCore.Plugin.RynthAi.dll` under `Runtime\Plugins\` and again as an **extra plugin path** (e.g. `C:\Games\Rynthcore\RynthCore.Plugin.RynthAi.dll`) — the loader staged both copies into the same per-session shadow directory using the **same file name**. The second `File.Copy(..., overwrite: true)` failed with *The process cannot access the file ... because it is being used by another process*, which cluttered logs and could confuse hot-reload behavior for plugins loaded afterward (e.g. **ub-Rythai** / `RynthCore.Plugin.UbRythai.dll`).

## Changes

1. **`PluginManager.LoadPluginsFromDisk`** — Skips an extra path when its file name (or canonical full path) was already loaded from the default plugins directory or from an earlier extra entry.
2. **`LoadedPlugin.ClearResolvedDelegates`** / **`PluginLoader.Unload`** — Clears resolved managed delegates before `FreeLibrary` so the native module reference count can drop cleanly on rescan.

## User action

If you intentionally need two different builds under the **same** DLL file name, remove the duplicate path from the launcher list or rename one DLL; the engine now loads at most one instance per file name per session.

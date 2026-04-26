# Changelog — MinHook preload before multi-client init (2026-04-22)

## Summary

- **EntryPoint:** `RynthCoreInit` now preloads `minhook.x86.dll` from the resolved engine directory **before** `MultiClientHooks.Initialize()` runs.
- **Reason:** Multi-client hooks call MinHook P/Invokes immediately; without an explicit `LoadLibrary` on the full path, Windows often searches from the **AC client** directory first, producing `Unable to load DLL 'minhook.x86.dll'` even when the file exists under `Runtime\`. The init thread’s later preload was too late for that first hook pass.
- **Build stamp:** `EntryPoint.BuildStamp` bumped to `2026-04-22-v55-minhook-before-multiclient`.

## User-visible effect

- Fewer spurious **“multi-client hook failed - Unable to load DLL 'minhook.x86.dll'”** lines when `minhook.x86.dll` is deployed next to the engine.
- Init thread still preloads MinHook (harmless duplicate load if already mapped).

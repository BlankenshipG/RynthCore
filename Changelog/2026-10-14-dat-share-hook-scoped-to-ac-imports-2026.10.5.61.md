# 2026.10.5.61 - engine: DAT share hook no longer intercepts Direct3D and driver file opens

Previous release: 2026.10.5.60.

## Problem

On one PC every RynthCore launch ended with "Could not initialize Direct3D. Please ensure that
DirectX 9.0 or higher is installed." (twice), then "The game encountered a fatal DirectX issue
while attempting to start...", and AC exited with code 1 about 6 seconds in. The same client
started from another launcher (no engine) worked. 2026.10.5.60 (saved UserPreferences.ini
recovery) did not change it.

In all three logs (builds 2026.10.14.2 and 2026.10.5.60) the error came 27-36 ms after the
engine started loading RynthChat, the plugin right after RynthAi. RynthAi took 378, 390 and
1175 ms to load, and the error moved with it, so AC's device creation was waiting on the engine
and failed as soon as it continued.

The DAT share hook (on with "Allow multiple clients") detoured the kernel32 `CreateFileA` and
`CreateFileW` exports with MinHook. That caught every caller in the process, including
d3d9.dll and the GPU driver, which open files (shader caches) while AC creates its device. Each
of those opens ran through managed code, which can wait on the runtime while plugins load, and
the detour did not hand back the Win32 last error: callers read it even after a successful
CreateFile (ERROR_ALREADY_EXISTS with OPEN_ALWAYS), and on some drivers that fails device
creation.

## Change

- The hook patches only the import slots of the modules that open AC's data files:
  `acclient.exe` (imports CreateFileA) and its CRT `MSVCR70.dll` (CreateFileA/W, used by
  fopen/_wfopen; the "W" DAT lines in the log come from here). Direct3D, drivers, the runtime
  and plugins call CreateFile without the engine in the way.
- The detour sets the Win32 last error back to the value CreateFile left before returning.
- The import slots are written back at engine shutdown (new step
  `DatFileShareHooks.Uninstall`), because the detours belong to the engine generation that hot
  reload unloads.
- A detour calls whatever its slot held before, so a Decal detour on the kernel32 export still
  runs.
- The startup log now lists the hooked slots, for example
  `Compat: DAT share - hooked MSVCR70.dll!CreateFileW import (...)`, and the DAT open lines
  (`Compat: DAT share - W [filtered]: client_portal.dat ...`) still appear when multi-client is
  on.

## Code

- `src/RynthCore.Engine/Compatibility/DatFileShareHooks.cs`: MinHook export hooks replaced by
  IAT slot hooks (`HookModuleImports`, `InstallSlot`, `Uninstall`, `IatHook` record); detours
  take the slot's original delegate and restore the last error (`SetWin32LastError`).
- `src/RynthCore.Engine/EngineLifecycle.cs`: `DatFileShareHooks.Uninstall` before
  `MH_DisableHook(ALL)`.

## Tests

- `dotnet build src\RynthCore.Engine\RynthCore.Engine.csproj -c Release`: 0 errors, no new
  warnings. No test project covers the hook; it needs a running client.
- Import tables checked on this PC: `acclient.exe` imports `KERNEL32!CreateFileA` (and
  `CreateFileMappingA`); `MSVCR70.dll` imports `KERNEL32!CreateFileA` and `CreateFileW`.

## For an affected PC before updating

Set `"EnableDatShareHook": false` in `%APPDATA%\RynthCore\engine.json` (or turn off "Allow
multiple clients"), then launch. If the client starts, this hook was the cause.

# Engine: object identity seeded at create, native crash logger made fatal-only

**Date:** 2026-10-04
**Branch:** `SK-local`
**Ships in:** next installer (2026.10.4.10; previous 2026.10.4.9)

## 1. Names and types readable from the moment an object is created

### Problem
Off-thread plugin reads (`TryGetObjectName` / `TryGetItemType`) are served only from the
main-thread identity snapshot, rebuilt every 500 ms. For about 10 s after login that snapshot
resolves almost no names (`[SnapshotDiag] ... names=1`) because qualities aren't populated yet.
The plugin's quick classify retries finish in about 10 ms, so every login-burst object without a
position (pack contents) gave up: about 200 `CLASSIFY-GIVEUP` lines per login, and items stayed
unclassified until a slow retry caught them.

### Fix
- `CreateObjectHooks` now passes the server's `PublicWeenieDesc` to the new
  `ClientObjectHooks.SeedIdentityFromCreate`. It runs on AC's main thread before the plugin is
  told about the object.
  - It reads `_name` (PWD +4) and `_type` (PWD +56). These are the same offsets the existing
    PWD-direct fallbacks use on the embedded copy.
  - Every read is validated: the descriptor must start with an acclient.exe vtable, and the
    string read is page-checked and length-capped.
- Off-thread lookups use these create seeds when the snapshot has no entry. For types, the seed
  is also used when the snapshot says `0` (qualities not loaded yet). The snapshot still wins
  whenever it has a real value.
- Seeds are removed in `DeleteObjectHooks`. A 16,384-entry cap clears them if a delete is ever
  missed.
- Diagnostics: `[CreateSeed] #1..5` sample lines, plus one
  `create-descriptor identity seeds active` summary, so the first in-game log confirms the
  offsets.
- Position is unchanged: the position snapshot is taken every 100 ms and already worked at login.

## 2. Native crash logger: fatal crashes only (`RynthCore.SehTrampoline.dll` v2)

### Problem
`CrashVeh` logged **every** first-chance access violation, including ones AC or our own `SEH_*`
wrappers catch. Its stack sweep then read 4,096 slots past ESP regardless of where the stack
ended, faulted off the top of the stack, and logged that fault too
(`SehTrampoline.dll+0x13E8`, dataAddr on a page boundary). Every run began with 3–4 noise
entries, and with the 24-entry cap a real fatal crash could find the log already full.

### Fix (`native/SehTrampoline/SehTrampoline.c`)
- **Stack sweep bounded** to `[Esp, NtCurrentTeb()->NtTib.StackBase)`, and skipped when ESP
  isn't inside the thread's stack.
- **Per-thread guards:**
  - `t_inLogger`: faults raised while the logger runs are never logged.
  - `t_sehDepth`: every `SEH_*` wrapper brackets its call, so AVs those wrappers catch are
    ignored.
- **Fatal-only routing:**
  - **Unhandled-exception filter** (`SetUnhandledExceptionFilter`, chained to the previous
    filter, with a re-entry guard): an unhandled exception is written immediately as FATAL.
  - **AVs inside NativeAOT code** (engine/plugins, detected via the `DotNetRuntimeDebugHeader`
    export) are written immediately. The runtime fail-fasts on these through
    `RaiseFailFastException`, which bypasses every filter. 0xC0000602 / 0xC0000409 are also
    written immediately.
  - **Every other first-chance AV** (acclient.exe, drivers, DINPUT8…) is formatted into an
    8-slot in-memory ring. It is written only if the process then dies: through the filter, or
    at `ExitProcess` (DllMain detach) if it happened within the last 30 s. That keeps
    crashes AC's own handler swallows. AVs AC handles and survives never reach the file.
- Separate caps: 16 immediate records and 8 fatal records per run. Formatting is now
  bounds-checked (8 KB per record).
- New export `RC_SehTrampolineVersion()` returns 2. The engine logs
  `native crash logger installed (v2, fatal-only)` at startup.

## Build
- `Build-SehTrampoline.ps1`: compiles clean (MSVC 14.50, x86, /W3).
- `RynthCore.Engine` Release build succeeds (no new warnings).
- Not yet verified in game.

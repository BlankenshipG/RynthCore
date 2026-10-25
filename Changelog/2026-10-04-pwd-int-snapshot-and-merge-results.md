# 2026-10-04 — PublicWeenieDesc int snapshot + merge-stack results (API v68)

## Problem

AutoStack failed almost every merge (one session: 515 attempts / 514 failures;
the day: 1,521 / 1,515 — mostly Prismatic Tapers and Diamond Scarabs).

* The RynthAi update loop runs on the plugin pump thread, not AC's main thread.
* Since audit finding #24 (no live AC memory reads off-thread),
  `TryGetObjectIntProperty` refused stack size / max stack size off-thread and
  had no snapshot to fall back on, so every stack read as "unknown" → 1.
* The engine's main-thread merge then read the *real* counts, found the target
  full and silently skipped it (logged only at Verbose). RynthAi waited its 10 s
  grace window and logged a failure — forever, for the same pairs.
* The same bad count fed AutoCram amounts, missile-crafting head/shaft counts
  and ExpressionEngine item-count expressions.

## Fix

### PublicWeenieDesc int snapshot (`ClientObjectHooks`)

* The 100 ms main-thread position walk (`PrefetchPositions` →
  `CapturePositionForId`) now also captures, per object, the PWD fields:
  container (+28), wielder (+32), valid locations (+40), location (+44),
  items capacity (+48), containers capacity (+52), stack size (+96),
  max stack size (+100) and material (+148).
* One `VirtualQuery` range check per object guards the whole field block; the
  walk stays allocation-free (struct values in pre-sized dictionaries).
* The snapshot is double-buffered and swapped together with the weenie-pointer
  buffers under `_weeniePtrSwapLock`, so readers always see one coherent frame.
* Off-thread reads route to the snapshot:
  * `TryGetObjectIntProperty` for STypes 6, 7, 9, 10, 11, 12, 131.
    Stack size / max stack of `<= 0` still prefer the appraisal cache.
  * `TryReadPwdInt32` (previously returned `false` off-thread).
  * `TryGetObjectOwnershipInfo` / `TryGetObjectWielderInfo` (container,
    wielder, location).
* Main-thread reads are unchanged (live PWD reads).
* A one-time `PWD int snapshot warm` log line confirms the snapshot is serving.

### Merge-stack results (`MergeStackResults`, API v68)

* New `MergeStackResults` table keyed by (source, target) records the outcome
  of every `MergeStackInternal` exit: `Queued`, `Sent` (with amount),
  `TargetFull`, `Failed`, `QueueFull`.
* New API slot `GetMergeStackResultFn` (appended; `PluginContractVersion` 68):
  returns the latest status plus amount and age in milliseconds.
* SDK: `RynthCoreHost.CurrentApiVersion = 68`, `HasGetMergeStackResult`,
  `GetMergeStackResult(...)` and `RynthCoreHost.MergeStackStatus` constants.
  Returns `None` on older engines, so plugins degrade to their old behaviour.

## Compatibility

* ABI: append-only; plugins built against v66/v67 keep working.
* No behaviour change on the main thread.

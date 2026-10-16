# 2026-10-04 — Fix: GetMergeStackResult stack corruption crash (API v69)

## Problem

Builds 2026.10.4.12 and .13 crashed a couple of seconds after AutoStack started
logging merges with garbage item names and IDs (e.g. `src=0x40C16E04 ->
tgt=0xFFFF7C95`, then `Event_StackableMerge from=0x40C16E04 to=0xBD8B1053`, a
target that didn't even match the request). The process died with no exception,
so the crash logger caught nothing and the launcher recorded a "normal close".

## Cause

`GetMergeStackResultCallbackDelegate` (PluginContract.cs) was the only one of the
API delegates without `[UnmanagedFunctionPointer(CallingConvention.Cdecl)]`. On
x86 that makes `Marshal.GetFunctionPointerForDelegate` produce a **stdcall**
thunk, while plugins call it as **cdecl**. Both sides popped the 16 bytes of
arguments, so RynthAi's stack shifted on every call. AutoStack (RynthAi 0.6.17+)
calls it on every pending-merge check, so locals turned to garbage until the
process died.

## Fix

* Added the missing `[UnmanagedFunctionPointer(CallingConvention.Cdecl)]`.
* API version bumped to **v69**. The SDK's `HasGetMergeStackResult` now requires
  v69, so a rebuilt plugin never calls the broken v68 thunk on a 4.12/4.13 engine
  (it falls back to the 10 s grace check).
* `Build-Release-All.ps1` now fails the release if any delegate in
  `PluginContract.cs` is missing the Cdecl attribute.

## ILT Hub draw diagnostics

* Engine logs `PluginManager: first RenderOverlay call for <plugin>` once per
  plugin per session, proving the overlay export is being driven.
* See RynthSuite `Changelog/RynthAi-0.6.19-hub-draw-diagnostics.md` for the
  plugin-side lines.

# 2026.10.5.52 - log the ILT verdict at Info

Previous release: 2026.10.5.51.

## Problem

A session's engine log never said whether the client counted the server as
Infinite Leaftide (ILT), or why:

- `IltInfo.Refresh` logged the verdict through `RynthLog.Compat`, and only when
  it flipped to ILT. A "not ILT" verdict never logged at all.
- With every category set to Trace under a global LoggingLevel of Info (the
  launcher's Logging page), category lines are written at Trace and dropped,
  so even the ILT line was missing.

## Change

- `RynthLog.Milestone`: an INFO line written whenever the global level allows
  Info, whatever the categories' emit levels are. It is meant for rare state
  changes a support log has to show.
- `IltInfo.Refresh` logs its verdict through it once per announced world and
  on every change. The line includes the reason (override, world name, host or
  RynthILT), whether RynthILT is loaded, what RynthILT says, and the saved
  per-character override, e.g.

  `IltInfo: world 'InfiniteLeaftide' is ILT-like (RynthILT loaded=True, says ILT=True, override=Auto)`

## Files

- `src/RynthCore.Engine/RynthLog.cs`
- `src/RynthCore.Engine/Compatibility/IltInfo.cs`

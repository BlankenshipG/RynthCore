# Engine — Nav panel waypoint list no longer jumps while editing

**Date:** 2026-10-04
**Branch:** `SK-local`
**Ships in:** next installer release (2026.10.4.8; previous 2026.10.4.7)

## Fixed
- **Nav panel (Avalonia "Nav" tab, `UI/Panels/NavPanel.cs`): the waypoint list kept scrolling
  back to the top, so points could not be selected or deleted.**
  - Cause: the 1-second poll timer rebuilt the entire panel every tick, including a brand-new
    waypoint `ScrollViewer`, so its scroll offset reset to 0 once a second. A rebuild that landed
    between mouse press and release also dropped the click on a row or its X button.
  - The waypoint `ScrollViewer` (and its row stack) is now created once and only refilled on
    rebuild; the scroll offset is saved and re-applied (immediately and after layout).
  - The poll only rebuilds when the plugin's nav JSON actually changed.
  - The poll does not rebuild while the mouse is over the waypoint list; the change is picked up
    on the first poll after the pointer leaves.
  - Rebuilds caused by your own edits (optimistic local updates) always resync on the next poll,
    so a command the plugin rejected can't leave stale state on screen.

## Not changed
- The ImGui Navigation window in the RynthAi plugin (`LegacyNavigationUi`) did not have this
  problem (ImGui keeps the list-box scroll position itself).

## Build
- `RynthCore.Engine`: `dotnet build -c Release` and NativeAOT `dotnet publish -c Release` succeed;
  no new warnings. The engine version is stamped by the release build (`-p:Version=yyyy.m.d.n`).

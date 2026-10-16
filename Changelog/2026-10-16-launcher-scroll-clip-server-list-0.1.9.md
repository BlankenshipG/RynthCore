# Launcher 0.1.9 — Launch Targets / Updates no longer clipped off-screen

## Summary

The Launcher tab looked like it had “lost” the server / launch-target list: Updates and Launch Targets were laid out above the visible clip and could not be scrolled back into view. Saved `appsettings.json` profiles were fine (still 5 servers / 7 accounts); only the UI scroll layout was wrong.

## Cause

Server Profiles and Account Profiles used a Grid `*` row inside the outer `ScrollViewer` → `StackPanel`. With infinite height available, that star row expanded incorrectly and pushed the cards above (Updates, Launch Targets) out of reach.

## Fix

- Server / Account profile grids: `Auto,Auto,*,Auto` → `Auto,Auto,Auto,Auto` with `MaxHeight="280"` on the ListBoxes so the outer scroll owns the page.
- Name the Launcher `ScrollViewer` and reset `Offset` to top on window open and when re-selecting the Launcher tab.

## Version

- Launcher HandWritten / Assembly: **0.1.9** (from 0.1.8).
- Hot ProductVersion when publishing: next stamp above live tip (e.g. `2026.10.16.16` if engine is `.15`).

## Notes

- Does not change `appsettings.json`. Restart the launcher to pick up the fix.
- No push / Games install until asked.

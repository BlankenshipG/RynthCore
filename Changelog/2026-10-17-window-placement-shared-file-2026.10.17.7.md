# 2026.10.17.7 — AC window placement: safe with many clients, never off-screen

**Kind:** Hot push (Engine). Previous `2026.10.17.6` (test installer).

## Problem

A tester's AC toolbar was missing: the bar with the panel buttons, indicators and shortcut bar.
Windows still opened from their keys. Nothing in RynthCore hides the toolbar:

- The radar and chat hiders capture only those elements.
- The vitals hider captures only the two vitals bars.
- The power-bar hider does nothing.

The toolbar is anchored to the AC window's bottom-right corner, so a window placed partly
off-screen hides it.

## Cause

- `%LOCALAPPDATA%\RynthCore\ac_window.txt` is shared by every client on the PC. Since 2026.10.17.5
  each client reads it and rewrites it every 10 seconds. With several clients this clashed, and
  the logs show "save placement failed — the file is being used by another process". A client
  could also read a half-written file.
- A restored placement was only checked for its centre being on a monitor. A saved rect bigger
  than the screen (another PC, a lower resolution, a bad read) could still put the window's
  bottom-right corner, and with it AC's toolbar, off-screen.

## Fix

- Placement writes are skipped when nothing changed. When something did change, the file is
  written to a temp file and swapped in, so it's never half-written; a clash is retried, then left
  for the next save. Reads allow sharing.
- A restored window (framed, kept-size borderless, and the framed rect borderless returns to) is
  moved, and shrunk if needed, to fit inside the monitor's work area. The log says when a saved
  rect didn't fit.

## If a toolbar is still missing

Settings > Display > Reset Window Borders, or `/rc window reset`, gives a framed window sized to
the screen and replaces the saved placement.

## Files

- `src/RynthCore.Engine/UI/AcWindowChrome.cs`

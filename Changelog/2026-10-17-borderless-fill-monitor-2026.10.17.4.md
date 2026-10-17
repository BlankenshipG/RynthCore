# 2026.10.17.4 — Borderless windowed fills the monitor; `/rc window reset`

**Kind:** Hot push (Engine). Previous `2026.10.17.3`.

## Problem

With Settings > Display > Borderless Windowed on (seen together with DXVK), the game sat at a
smaller resolution than the screen with no way to resize it.

## Cause

Borderless only removed the window frame (caption, sizing border, maximize box) and kept the
window at AC's resolution (for example 1760x990 on a 1920x1080 monitor). With no border to drag and
no maximize, nothing could make it bigger. AC also re-sizes its window to its own resolution after
a device reset, which DXVK triggers, so the window could shrink again later. Remember layout then
saved that frame-less rect as the window placement.

## Fix

- Borderless on now fills the monitor the window is on (borderless fullscreen). It remembers the
  framed size and position first.
- If AC shrinks the borderless window again, it is stretched back to fill the monitor, up to 4
  times a minute. After that it is left alone, with a log line pointing at `/rc window reset`.
- Borderless off puts the frame back at the remembered size. With nothing remembered (after a hot
  reload) the window comes back at three quarters of the monitor, centred and resizable.
- Remember layout no longer saves the window rect while it is frame-less, and it doesn't restore
  a saved rect when borderless is on.
- New `/rc window reset` turns borderless off and gives a normal, framed, resizable window,
  centred. New `/rc borderless on|off` does the same as the Settings toggle.

## Files

- `src/RynthCore.Engine/UI/AcWindowChrome.cs`
- `src/RynthCore.Engine/Compatibility/RynthCoreChatCommands.cs`

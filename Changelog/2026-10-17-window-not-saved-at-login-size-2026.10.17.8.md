# 2026.10.17.8 — Clients no longer start at AC's 800x600 login size

**Kind:** Hot push (Engine). Previous `2026.10.17.7`.

## Problem

Silentkelpie (and Silentstorm) started in a small window.

## Cause (from the 08:40–08:44 logs)

- At the login and character-select screens AC shrinks its window to an 800x600 game area
  (806x629 with the frame). The placement saver ran on those screens too, for example while a
  client sat at character select for a held engine reload. It wrote 806x629 to the shared
  `%LOCALAPPDATA%\RynthCore\ac_window.txt`.
- Every client then restored 806x629 after entering the world: "restored AC window to (557,208)
  806x629" for both Kelpie (pid 37756) and Storm (pid 41444).
- They looked different because of when the restore landed:
  - Kelpie's came 10 s after entering the world, while AC was still applying its 1760x990
    resolution, so AC laid out its 3D view for the smaller window (viewport 1514x870).
  - Storm's came 26 s in, after AC had finished, so its 1760x990 picture was only scaled into the
    small window (viewport 1759x990).
- Storm's in-world 800x600 window was then saved again (08:44:43), so the bad size kept coming back.

## Fix

- The placement is saved only in the world, and never when the game area is 800x600.
- A saved framed rect with an 800x600 game area is ignored at restore (logged), and so is an
  800x600 borderless rect. AC keeps its own size, so the 806x629 already in the file no longer
  shrinks anyone.
- Save Window Position says "not in the world" when it can't save.

## Files

- `src/RynthCore.Engine/UI/AcWindowChrome.cs`

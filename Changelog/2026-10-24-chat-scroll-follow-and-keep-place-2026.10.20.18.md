# RynthChat: following the newest line and keeping your place when scrolled up

Reported: the chat "auto-scrolled to the top" instead of following the bottom, and a manual
scroll didn't keep its place: it skipped back to the top or the bottom.

Seen live (2026-10-24): following stopped by itself as soon as the mouse rested over the chat,
without any wheel or click, and from then on the view crept up as a full buffer dropped its
oldest lines, until it sat at the top showing ten-minute-old lines.

## Cause

`ChatFace.DrawLines` decided "the player scrolled" from any change of the list's scroll between
frames, and also only moved the view itself when lines dropped off the top. Any scroll change
the player didn't make (a hover or focus change, a re-measure, ImGui clamping the scroll to new
content) therefore turned following off, or moved the view the player was reading.

## Fix

- Only the player moves the list: the wheel over it, a press on its scrollbar (held until the
  button comes up), or a drag-select past its edges. Only those turn following off (wheel up, or
  leaving the bottom) or back on (back at the bottom).
- Following: the view is put at the bottom every frame unless the player is scrolling.
- Not following: the view is put back where the player left it every frame, moved only by how
  much the lines above it changed (dropped from a full buffer, re-measured after a resize or a
  rule change), so nothing else can move it.
- Kept: the place is kept across a route rebuild (another client's settings, a rule edit) by
  the line at the top of the view.

## Files

- `src/RynthCore.Engine/ImGui/Panels/ChatFace.cs`

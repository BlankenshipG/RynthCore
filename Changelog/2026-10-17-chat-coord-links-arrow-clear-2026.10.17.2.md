# 2026.10.17.2 — Click coordinates in chat for RynthNav's arrow; Clear on the arrow

**Kind:** Feature (Engine). Previous `2026.10.16.24`.

## What

- **Coordinates in RynthChat are links.** Any map coordinate in a chat line — the game's,
  someone's tell, or one you pasted (`82.3n,34.2e`, `42.1N 33.6E`, `33.6E, 42.1N`) — is drawn
  underlined in teal. Hover for the hand cursor and a tooltip.
  - **Click:** RynthNav's floating arrow points there (its overlay opens: direction relative to
    the way you face, distance, compass point; green within 30 yd).
  - **Ctrl+click:** RynthNav walks there (portals and recalls when its routing is on).
  - Right-click still has the line menu ("Arrow to" / "Go to" and the rule options); a drag still
    selects and copies lines.
- **Clear on the arrow overlay** (eraser, top right): clears the target (`/rnav arrow clear`),
  stops a walk to it, and hides the overlay.
- **The overlay closes itself** about 4 s after its target goes away — you arrived (RynthNav
  counts 10 yd as arrived; `/rnav arrive <yd>` changes it) or it was cleared elsewhere. It shows
  "Done: <target>" meanwhile. Opened empty to type coordinates, it stays open.
- Go (▶) on the overlay remains the "auto-nav to it" button; Plan shows the route first.

## Notes

- Needs the RynthNav plugin; without it the links say so instead of pointing anywhere.
- Coordinates are found when a line is measured (never per frame), with a cheap pre-check before
  the parser.
- AC's own (retail) chat window can't be clicked; the links are in RynthChat.

## Checks

Engine builds; ChatCoordTests 49/49 (adds the lowercase / no-space forms).

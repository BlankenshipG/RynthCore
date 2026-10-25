# Nav panel: Breadcrumbs and Route overlay toggles (RynthAi 0.5.9-legacy-ui)

Date: 2026-10-05
Branches: RynthCore `merge/aelrynth-2026.10.5.1`, RynthSuite `fix/rynthai-launcher-remote-commands`

## What changed

### Nav panel (engine, `NavFace`)
A new row under Route / Insert has two checkbox-style toggles. A ticked box and teal text means
on. Both are saved per character in RynthAi's settings.
- **Breadcrumbs**: records the trail you walk and draws it on the ground. Turning it off stops
  recording and hides the trail. Turning it back on starts a new trail segment, so the trail
  never draws a line across the stretch you walked while it was off.
- **Route overlay**: draws the route in the world: waypoint rings and connecting lines, waypoint
  labels, and the guide line to the active waypoint. The waypoint HUD window keeps its own
  switch (`/ra navhud`).

### RynthAi
- `NavOverlaySettings.ShowRouteMarkers` now does something. It was saved but never read, so
  route rings always drew. `NavMarkerRenderer` now skips the rings and lines when it is off,
  and the waypoint labels and guide line follow it too.
- `NavOverlaySettings.TrackBreadcrumbs` now also hides the drawn trail when off.
- Nav bridge: the snapshot carries `TrackBreadcrumbs` and `ShowRouteOverlay`. New commands
  `setBreadcrumbs` and `setRouteOverlay` (with `On`) save the settings.
- Chat commands:
  - `/ra navtrail on|off|toggle` (the existing `clear` and the status report still work; the
    report now says whether tracking is on).
  - New `/ra navoverlay [on|off]` (no argument toggles).

### Compatibility
- An older RynthAi doesn't send the two flags, so both toggles show off; clicking them does
  nothing until RynthAi is updated.

## Versions
- RynthAi plugin: 0.5.9-legacy-ui (previous 0.5.8-legacy-ui).
- The next installer is 2026.10.5.9 (previous 2026.10.5.8).

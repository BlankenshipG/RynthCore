# Nav editor expansion, waypoint overlay/HUD and breadcrumb tracker

**Date:** 2026-10-04
**Component:** RynthAi plugin (RynthSuite `Plugins/RynthCore.Plugin.RynthAi`)
**Version:** 0.6.29 → 0.6.30

## Summary

The Navigation tab has been rebuilt around the editing features UtilityBelt-IT
added to VTank-style navs. On top of that come an on-screen waypoint overlay (HUD,
step labels, guide line) and a breadcrumb tracker that records where you have
walked and can turn that trail into a route.

## Nav editor (Navigation tab)

- **File row:** New (with confirmation), Save (opens Save As for an unsaved route),
  Save As (sanitised file name, confirms before overwriting) and Reload.
- **Insert position:** new steps go at the End, Above the selection or Below it,
  like UB-IT's "insert before".
- **Add steps:** Point (your position), Snap to path (inserts into the nearest
  segment), Pause, Chat (adds the leading `/` automatically), Recall (pick from the
  spell list or type an id), Portal (uses the selected object) and Point at the
  selected object.
- **Edit steps:** Up, Down, Duplicate, Delete (or the Delete key), Set active,
  Select nearest, Reverse route, and select All or None.
- **Step list:** multi-select with Ctrl or Shift, double-click a step to make it
  active, and right-click for a context menu.
- **Step editor:** shown when one step is selected. Edit NS/EW/height, "Set to my
  position", pause duration, chat text, recall spell and portal target ("Use
  selected object").
- Edits are copy-on-write, so the navigation engine never sees a half-edited
  route. The active step follows its waypoint when steps are moved.

## Recording (Record tab)

- **Record mode** (UB-IT KeyStoreRecorder style): adds a Point every N yards while
  you walk.
- **Portal capture:** when you use a portal while recording, it adds a Portal step
  for the portal you selected in the last 20 seconds, followed by an optional
  pause.
- **Saving:** recording autosaves every 10 steps and again when it stops (or when
  the session ends).

## Breadcrumb tracker

- **Trail:** records your path at a set spacing. A portal trip or teleport starts
  a new trail segment instead of drawing a line across the map.
- **Drawing:** the trail is drawn in the world with a fade, within a set range,
  using Nav3D (or the ImGui fallback when Nav3D isn't available).
- **Trail → route:** turns the latest trail segment into a new Once route.
- **Backtrack route:** the same as Trail → route, but reversed, to walk back the
  way you came.
- **Clear trail.**

## Waypoint overlay and HUD

- **Nav HUD window:** route name and type, recording state, current step and its
  type, distance with an up/down height hint, a direction arrow relative to your
  facing, the next step, remaining or loop distance, nav status and trail stats.
- **Step labels:** "#index  Nyd" labels on the next N Point steps.
- **Guide line:** a line from you to the active waypoint, like UB-IT's yellow
  breadcrumb line.

## Display options (Display tab)

- Route markers on or off, and how many markers to draw around the active step.
- Ring and line thickness, and height offset.
- Colours for rings, the active ring, lines, the guide line and the trail, plus
  Reset colours.
- HUD visibility, including a "only while navigating" option.

## Tools tab

- **Simplify route:** Douglas–Peucker with an adjustable tolerance. Removes
  redundant Point steps and keeps every action step.
- **Height repair:** "Select raw-height steps" and "Divide Z by 240".

## Bug fix: waypoint height

The Add-point button, the `/ra addnavpt` command and the web bridge saved the raw
world height instead of height ÷ 240 (the VTank .nav unit). Those waypoints drew
their markers far above the ground. All three now save the correct value. Use the
height repair tool to fix routes that were already saved.

## New chat commands

- `/ra navrec [on|off]`: toggle route recording.
- `/ra navhud [on|off]`: show or hide the nav HUD.
- `/ra navtrail [clear]`: show trail stats, or wipe the trail.

## Files

- New: `LegacyUi/NavOverlaySettings.cs`, `LegacyUi/NavRouteEditing.cs`,
  `NavBreadcrumbTracker.cs`, `NavOverlayRenderer.cs`
- Rewritten: `LegacyUi/LegacyNavigationUi.cs`
- Modified: `LegacyUi/LegacyUiSettings.cs`, `LegacyUi/LegacyDashboardRenderer.cs`,
  `LegacyUi/NavCoordinateHelper.cs`, `NavMarkerRenderer.cs`, `RynthAiPlugin.cs`,
  `RynthAiCommands.cs`, `RynthCore.Plugin.RynthAi.csproj`

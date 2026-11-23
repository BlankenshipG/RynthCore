# Breadcrumbs and Route overlay on Settings > Navigation (2026.10.5.62)

Date: 2026-10-15

| Component | Version | Previous release |
|---|---|---|
| RynthCore.Engine | 2026.10.5.62 | 2026.10.5.61 |

## What's new

Settings > Navigation now has the Nav panel's two overlay switches, at the top of "Nav Marker
Display":

- **Breadcrumbs**: record the trail you walk and draw it on the ground (`/ra navtrail on|off`).
- **Route Overlay**: draw the route in the world, meaning the waypoint rings and lines, the
  waypoint labels and the guide line (`/ra navoverlay on|off`).

They are the same switches as on the Nav panel, not copies. A change in either place shows in the
other:

- The Nav panel's toggle also asks the Settings page to fetch again. It updates the Nav panel's
  own settings copy too, so a nav point reach saved straight afterwards doesn't send the old value
  back.
- A save from the Settings page also asks the Nav panel to fetch again.

Both panels share the tooltips (`SettingsSchema.BreadcrumbsTip` and `RouteOverlayTip`).

## Compatibility

The new fields (`TrackBreadcrumbs` and `ShowRouteOverlay` in `RynthAiSettings`) are nullable.

- **Older RynthAi:** it doesn't send them, so the two rows stay hidden, and a save sends null,
  which RynthAi reads as unchanged.
- **RynthAi 0.5.70-legacy-ui or later:** sends and applies them.

## Files

- `src/RynthCore.Engine/UI/Data/SettingsData.cs`: the fields, the two rows, the shared tooltips,
  and the Nav refresh after a save.
- `src/RynthCore.Engine/ImGui/Panels/NavFace.cs`: the shared tooltips and the local settings copy.
- `src/RynthCore.Engine/UI/Data/RynthAiData.cs`: a Settings refresh after each nav command.

## Install

The installer build (`installer\Build-Installer.ps1`) stamps the engine as 2026.10.5.62.

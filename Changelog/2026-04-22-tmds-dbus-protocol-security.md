# Changelog — Tmds.DBus.Protocol security bump (2026-04-22)

## Summary

- **Packages:** Pinned **Tmds.DBus.Protocol** to **0.21.3** on projects that pull it transitively through Avalonia (Avalonia UI stack / FreeDesktop).
- **Reason:** NuGet audit **NU1903** — high severity [GHSA-xrw6-gwf8-vvr9](https://github.com/tmds/Tmds.DBus/security/advisories/GHSA-xrw6-gwf8-vvr9). Versions below **0.21.3** on this package line are affected; **0.21.3** is the maintained backport compatible with Avalonia 11.2.x.
- **Projects:** `RynthCore.App.Avalonia`, `RynthCore.Engine`.
- **Build stamp:** `EntryPoint.BuildStamp` set to `2026-04-22-v56-tmds-dbus-protocol-0213`.

## User-visible effect

- No functional change expected for normal Windows desktop use; dependency graph uses a patched protocol assembly and restores without the NU1903 advisory for these projects.

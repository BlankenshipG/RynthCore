# HookResolver UI 1.0.1: Tmds.DBus.Protocol security update

Released 2026-10-10. Previous release: 1.0.0 (no version was set; the SDK default).
Developer tool only: nothing that ships in the installer changes.

## What's fixed

- **Dependabot alert #1 (high), GHSA-xrw6-gwf8-vvr9.** Through Avalonia (Avalonia.FreeDesktop),
  `tools/HookResolver.UI` pulled in `Tmds.DBus.Protocol` older than 0.21.3. In those versions a
  malicious D-Bus peer can spoof signals, exhaust file descriptors and cause a denial of service.
  D-Bus is only used on Linux, so the Windows tool was never exposed, but the alert is real for the
  manifest.
- The project now references `Tmds.DBus.Protocol` 0.21.3 directly, the same override the engine
  and the launcher already have.

## Files

- `tools/HookResolver.UI/RynthCore.HookResolver.UI.csproj`: direct `Tmds.DBus.Protocol` 0.21.3
  reference; `Version` 1.0.1.

## Checks

- Release build succeeds with no warnings or errors (no NU1903).
- `dotnet list package --include-transitive` resolves `Tmds.DBus.Protocol` 0.21.3.

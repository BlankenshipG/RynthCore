# Security: pin Tmds.DBus.Protocol 0.21.3 (NU1903)

**Date:** 2026-10-04
**Branch:** `fix/tmds-dbus-advisory` (based on `main`; not merged)
**Advisory:** NU1903 / [GHSA-xrw6-gwf8-vvr9](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9) (high severity)

## Problem
Avalonia 11.x (through `Avalonia.FreeDesktop`) depends on `Tmds.DBus.Protocol` 0.20.x, which has a
known vulnerability. NuGet restore reported NU1903 for every project that references Avalonia.

## Fix
A direct `PackageReference` to `Tmds.DBus.Protocol` **0.21.3** overrides the vulnerable transitive version:

- `src/RynthCore.App.Avalonia/RynthCore.App.Avalonia.csproj`
- `src/RynthCore.Engine/RynthCore.Engine.csproj`

The same pin is in RynthSuite (`Tools/RynthCore.LootEditor`, `Tools/RynthCore.MonsterEditor`) on `feat/ilt-hub`.

## Verification
- `dotnet restore` / `dotnet list package --include-transitive` resolve `Tmds.DBus.Protocol 0.21.3` in all projects.
- No NU190x warnings remain.
- RynthAi 0.5.2 builds and publishes (NativeAOT) against this branch.

## Notes
- No runtime behaviour change on Windows. D-Bus is only used by Avalonia on Linux desktops.
- Drop the explicit pin once Avalonia ships a release that depends on a fixed version.

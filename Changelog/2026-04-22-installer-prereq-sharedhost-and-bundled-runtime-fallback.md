# Changelog — Installer .NET 10 check + self-contained host fallback (2026-04-22)

## Summary

- **Inno (`RynthCore.iss`):** The prerequisite page for the x64 .NET 10+ Desktop/Loot-Monster tool chain now also treats a valid install as **HKLM64\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost** (and the parallel `SOFTWARE\Microsoft\dotnet\Setup\...` key when present) when **Version** is **10+**. This matches how the runtime advertises the shared host, not only `...\shared\Microsoft.WindowsDesktop.App\...` or `InstalledManifests` (SDK) entries.
- **Build-Installer (`Build-Installer.ps1`):** Launcher and Injector `dotnet publish` for the bundle add **`-p:SelfContained=true -p:PublishSelfContained=true -p:RollForward=Disable`**. Self-contained apphosts should not roll forward to **%ProgramFiles%\dotnet\shared\...** when a matching **major** is bundled next to the executable.

## Reason

Users reported the app (or the installer) still “checking for .NET in the wrong place.” The registry under **Setup\InstalledVersions\x64\sharedhost** is a primary signal for a machine .NET host install. Separately, a self-contained build can still consult global locations unless **RollForward** is set to not roll to a **shared** host install.

## Follow-up

Rebuild the bundle with `Build-Installer.ps1 -Version <bundle>` and verify on a machine **without** a user-visible **WindowsDesktop.App** shared folder but **with** a normal .NET 10+ runtime (sharedhost) install.

---

**Related assembly:** `RynthCore.App.Avalonia` **0.1.2** (Informational: `0.1.2-scd-rollforward-disable`).

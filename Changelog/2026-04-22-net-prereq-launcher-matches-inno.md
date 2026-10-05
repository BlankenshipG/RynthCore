# Changelog — Launcher .NET 10+ x64 probe matches Inno (2026-04-22)

## Problem

The installer’s prerequisite page used specific **HKLM64** rules for the **Loot/Monster** x64 FDD tools, while the launcher only reported **x86** self-contained **sidecar** files (`RynthCore.runtimeconfig.json` / `deps.json`). That was a different notion of “runtime” and was easy to confuse with the setup’s **registry** check.

## Change

- **`src/RynthCore.DesktopLog/NetDesktopX64Prerequisite.cs`:** Implements the same three registry paths as **`installer/RynthCore.iss`**: `Microsoft.WindowsDesktop.App` 10+ folders, `InstalledManifests\x64` **manifest-10.**, and **`sharedhost` Version** 10+ under `dotnet\Setup` and `Microsoft\dotnet\Setup`. Uses **`RegistryView.Registry64`**. On 32-bit OS, returns “satisfied” (N/A) like Inno.
- **`LauncherHostDiagnostics`:** The activity line and `RynthCore-Launcher.log` now include a clear **x64 suite tools** line with the same result as setup, plus the official download URL when not detected.
- **`RynthCore.iss`:** Comment points maintainers to **`NetDesktopX64Prerequisite.cs`**.

## Versions

- **RynthCore.DesktopLog** `1.0.0` → **`1.0.1`**
- **RynthCore.App.Avalonia** `0.1.2` → **`0.1.3`**

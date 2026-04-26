# Launcher + Injector: self-contained publish (2026-04-22)

## Problem

`RynthCore.exe` and `RynthCore.Injector.exe` were built **framework-dependent**. On PCs without a global **.NET Desktop Runtime 10 (x86)**, Windows shows the standard apphost dialog (“You must install .NET to run this application”) — that is the OS host, not C# code in the app.

## Change

- **`installer/Build-Installer.ps1`:** `dotnet publish` for `RynthCore.App.Avalonia` and `RynthCore.Injector` now uses **`--self-contained true`** (still **win-x86**). The install layout carries the .NET host + runtime next to the apphosts; **no separate x86 runtime install** is required to run the launcher or injector from the game folder.
- **`installer/staging` prerequisites text:** `RynthCore-Prerequisites.txt` updated — x64 **only** for optional **LootEditor** / **MonsterEditor** (still FDD).
- **`installer/RynthCore.iss`:** Removed the Start Menu shortcut **“Install .NET Desktop Runtime (Download)”** so the primary experience does not push a global .NET install for the main app.
- **`BUILD.md`:** Aligned with the same story.
- **`RynthCore.App.Avalonia`:** Version **0.1.0 → 0.1.1** (informational tag `0.1.1-launcher-scd-installer`).

## Tradeoff

Installer and on-disk size grow (bundled .NET 10 x86 for two executables). This matches the request that the **app** not depend on a preinstalled runtime; only the **optional** x64 tools still document a separate download.

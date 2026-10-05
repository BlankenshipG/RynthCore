# RynthCore launcher — host / runtime diagnostics

Date: 2026-04-23

## Summary

The Avalonia launcher (`RynthCore.App.Avalonia`, shipped as `RynthCore.exe` in the bundle) now logs **host resolution** details at startup:

- **Activity panel:** one short line (`Host: … runtimeconfig=… deps=…`) so operators see sidecar status without opening log files.
- **Desktop log** (`RynthCore-Launcher.log`): multi-line block with `ProcessPath`, base directory, framework/OS/arch, CLR version, and presence of `RynthCore*.runtimeconfig.json` / `RynthCore*.deps.json` (including legacy `RynthCore.App.Avalonia.*` names for mismatch triage).

## Why

After renaming the published apphost to `RynthCore.exe`, mismatched host metadata filenames can cause false “install .NET” prompts. Sidecar logging makes that state obvious in the field.

## Version

- `RynthCore.App.Avalonia` NuGet-style `Version`: **0.1.0** (`InformationalVersion`: `0.1.0-launcher-host-diagnostics`).

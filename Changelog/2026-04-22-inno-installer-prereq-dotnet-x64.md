# Inno Setup — .NET 10+ Desktop (x64) prerequisite page (2026-04-22)

## Summary

`installer/RynthCore.iss` now includes a custom wizard page (after **Welcome**) that:

- Detects .NET 10 in the 64-bit registry in **either** way:
  - `HKLM\SOFTWARE\dotnet\shared\Microsoft.WindowsDesktop.App\` (version folder major ≥ 10), or
  - `HKLM\SOFTWARE\Microsoft\dotnet\InstalledManifests\x64\` — any subkey name containing `Manifest-10.` (e.g. `...Manifest-10.0.100` under **InstalledManifests\x64** in `regedit`).
- Offers **Open download page** (Microsoft .NET 10 download hub) and **Check again** after the user installs.
- **Disables Next** on 64-bit Windows until the runtime is present, so file copy cannot start without it.
- On **32-bit** Windows, skips the requirement (tools are x64-only; `RynthCore.exe` still installs).
- On **silent** install (`/SILENT`, `/VERYSILENT`), **skips** the page so automation is not blocked; the admin must pre-provision the x64 runtime for the editors.

RynthCore.exe / Injector remain self-contained; this check is only for the bundled **x64** tool exes.

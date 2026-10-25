# Launcher 0.1.8 — Enable DXVK toggle + fetch / installer seed

## Summary

Adds an opt-in **Enable DXVK (Vulkan D3D9)** launch-behavior checkbox. When on, the launcher installs DXVK’s x86 `d3d9.dll` next to `acclient.exe` (and removes a Rynth-managed copy when off). Package cache: `<RynthCore>\Runtime\dxvk\` — seeded by the installer or fetched from GitHub (`Fetch / update DXVK` / `scripts\Fetch-Dxvk.ps1`).

## Changes

- `AppSettings.EnableDxvk` (default **false**).
- `DxvkPackageService` — cache, apply/remove with preexisting park, GitHub latest fetch (x32 only).
- `AcClientLaunchSettingsService.Apply` — applies DXVK with intro/multi-client settings.
- Launcher UI: checkbox + Fetch button + cache status line.
- `scripts\Fetch-Dxvk.ps1` — CLI/installer seed helper.
- `Build-Installer.ps1` — stages `Runtime\dxvk` into the core payload.
- Docs: `docs/DXVK_TEXTURE_SMOKE.md` updated for launcher path.

## Notes

- Engine `DxvkDetection` (2026.10.16.13) already forces software overlay textures when DXVK is active.
- Toggle requires **AC relaunch** to pick up / drop the drop-in DLL.
- Does not push to remotes; hot-publish launcher separately when ready.

## Version

- Launcher HandWritten / Assembly: **0.1.8** (from 0.1.7).

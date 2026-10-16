# 2026.10.16.13 — DXVK detection + software overlay policy

## Summary

Adds a one-shot `DxvkDetection` probe for drop-in / non-system `d3d9.dll` (DXVK smoke path). When DXVK or another game-dir d3d9 is active, Rynth forces the software Avalonia overlay (`CreateTexture` + `LockRect`) and skips D3D9Ex shared-HANDLE uploads. Smoke checklist: `docs/DXVK_TEXTURE_SMOKE.md`.

## Changes

- `D3D9/DxvkDetection.cs` — module path + FileVersionInfo / `dxvk.conf` probe; Milestone log lines for grepping.
- `D3D9Bootstrapper` — probe + overlay policy as soon as `d3d9.dll` is loaded (Decal and post-login paths).
- `AvaloniaOverlay` — prefer software bridge when DXVK / non-system d3d9 is detected.
- Docs: `docs/DXVK_TEXTURE_SMOKE.md` (DLL layout, log greps, pass/fail).

## Version

- Engine ProductVersion: **2026.10.16.13** (hot push from 2026.10.16.12).

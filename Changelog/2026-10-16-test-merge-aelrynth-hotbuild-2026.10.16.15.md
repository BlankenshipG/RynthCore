# 2026.10.16.15 — Test merge: daily hotbuild onto aelrynth/main

**Branch:** `test/hotbuild-2026-10-16-on-aelrynth`  
**Base:** aelrynth `2026.10.15.6` (`a57049d`) + Suite `2026.10.15.7`  
**Incoming:** `test/hotbuild-2026-10-16` (today’s Core + Suite work)

## What

Local install of the resolved merge for in-game validation:

- DXVK detect / software overlay; launcher DXVK toggle
- Mini Remote dock/undock + Options
- Oracle Skills embed (`/rc oracle-skills`), Inventory Count/GInv, Radar→RynthNav
- Chat combat standard filters
- RynthAi 0.5.79 PullAway kite

## Conflicts resolved (Core)

- `DatFileShareHooks.cs` — keep scoped IAT + private CreateFileW (hotbuild)
- `EngineSettings.cs` — Borderless/Remember + MaxHotReloads default 20 (aelrynth)

Suite merge was clean.

## Deploy

`Publish-EngineCoreClr.ps1 -EngineOnly -AllowBehind -MsBuildArgs @('-p:Version=2026.10.16.15')`  
Ai publish with `-p:Version=2026.10.16.15` → `C:\Games\RynthSuite\RynthAi\`

# 2026.10.16.16 — Sense → RynthNav; Vision / Tracker / ILT → Oracle

**Previous:** 2026.10.16.15 (test-merge install). Hot push.

## Summary

Same pattern as Radar → RynthNav: remove top-bar icons, keep every open path
(`PanelRouter`, OverlayHost, `/rc …`, Char menu), and put header toggles on the
host panel.

## Moves

| Panel / icon | Was | Now |
|---|---|---|
| **Sense** | ImGui-only bar button | RynthNav header (with Radar / Mini / Map) + `/rc sense` |
| **Vision** | OverlayHost bar button | Oracle companion strip (above Oracle tabs) |
| **Tracker** | OverlayHost bar button | Oracle companion strip |
| **ILT leaf** (Bounties / Guardian) | Top-bar leaf button | Oracle companion strip (leaf + menu) |

## Linkages kept

- `PanelRouter.Toggle` / `/rc ui open …` / panel restore / Avalonia faces
- `/rc sense`, `/rc radar` unchanged
- Char menu: Sense under Nav family; Vision / Tracker / Bounties / Guardian listed
- Oracle Skills embed and Oracle bar button unchanged (Oracle stays on the bar)

## Files

- `ImGuiBar.cs` — LauncherOnly + Sense off ImGuiOnlyBarPanels; ILT leaf off bar
- `RynthNavFace.cs` — Sense header toggle
- `OracleCompanionStrip.cs` + `ScriptWindowFace.cs` — Vision / Tracker / ILT strip
- `RynthAiFace.cs` — Char menu entries

## Note

Engine hot-reload of this build crashed one client at 2026-10-16 17:11 (FileWatcher →
Engine.dll). Prefer a full relog over mid-session Engine copy when testing.

## Deploy

`dotnet publish` Engine `-p:Version=2026.10.16.16` → `C:\Games\RynthCore\Runtime\`

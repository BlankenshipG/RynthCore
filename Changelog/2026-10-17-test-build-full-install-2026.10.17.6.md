# 2026.10.17.6 — test build full install

**Kind:** Hot push (test build installer). Previous installer `2026.10.16.24`.

Built from `test/hotbuild-2026-10-16-on-aelrynth` (Core `b0fda86`, Suite `7877c86`), with rynth's
release `2026.10.17.1` (`aelrynth/main` Core `8330793`, Suite `c7807b6`) merged in.

## New since 2026.10.16.24

- **Window (engine 2026.10.17.4 / .5):**
  - Borderless windowed fills the monitor, so it no longer gets stuck small (DXVK).
  - Settings > Display gains **Reset Window Borders**, **Save Window Position**, and **Borderless
    Fills Monitor** (off keeps the framed window's size and spot).
  - The borderless position is remembered across launches.
  - New commands: `/rc window reset|save|fill on|off` and `/rc borderless on|off`.
- **Engine settings (2026.10.17.3):** `engine.json` saves only the keys a client changed, so other
  clients (relaunched for DXVK) no longer reset in-game engine options.
- **Chat / RynthNav (2026.10.17.2):** coordinates in RynthChat (`82.3n, 34.2e`) are links. Click to
  point RynthNav's arrow, Ctrl+click to go there. The arrow overlay has Clear and closes itself on
  arrival.
- **RynthAi 0.5.82:** a profile that can't be read falls back to its `.bak`, or keeps the
  unreadable file aside, instead of being overwritten with defaults. Saves are atomic.
- **RynthOracle 0.4.0:** Legendary quests tab (what you have, what's missing, time left).

## From rynth's 2026.10.17.1 (upstream)

- Hotkeys, Damage panel and Items panel updates; RynthAi buff One-Touch heal, weapon planner,
  mana stones, and the action pacer.
- Monster rules gain **Prefer** (preferred damage kind) alongside this branch's **Power** and
  **Kite**. The merge kept all three in the rule copy, the change check, the detail view and the
  VTank import.

## Checks

- Engine and RynthAi build clean after the merge.
- HotkeyTests 924/924; EngineSettingsTests 226/226.
- RynthAiHostTests 1087/1089. The 2 known failures are unchanged: map bake needs the dats, and the
  mini remote help line.

## Install

Close every AC client first (the loader and engine are replaced).

- `installer\Output\RynthCore-Setup-2026.10.17.6.exe`
- `installer\Output\RynthCore-2026.10.17.6-deploy.zip`

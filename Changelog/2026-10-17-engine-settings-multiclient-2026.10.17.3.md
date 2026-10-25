# 2026.10.17.3 — In-game engine settings no longer reset by other clients

**Kind:** Hot push (Engine). Previous `2026.10.17.2`.

## Problem

Engine options set in game (Settings > Display: borderless, remember layout; UI hooks, ImGui,
Auto-ID mode, close on disconnect, ILT names, plugin list) came back reset, notably after relaunching
clients for a DXVK change.

## Cause

Every client keeps the whole `%AppData%\RynthCore\engine.json` state in memory and, on any save,
wrote all of the engine's keys back from it. A client that never touched a setting put its stale
value over another client's change — last writer won. Relaunching some clients (DXVK on/off) while
others kept running made it show. (RynthChat had the same flaw; fixed in 2026.10.16.23.)

## Fix

The engine tracks which of its keys this client changed this session and writes only those;
every other key keeps what the file holds (`EngineSettingsFile.Merge` `changedKeys`). The launcher's
keys were already kept.

## Checks

EngineSettingsTests 224/224, including a two-client case: another client's change survives this
client saving a different setting.

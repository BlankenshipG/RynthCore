# 2026.10.16.20 — Plugin chat queue keeps long server replies

**Kind:** Hot push (Engine).

## Problem

Incoming chat waits in a queue until the plugin pump hands it to the plugins. The queue held
256 lines and silently dropped the oldest. A `/myquests` reply (one line per quest flag,
often several hundred) arriving while the pump was slow lost its head, so RynthOracle's
second quest refresh in a session came back mostly empty.

## Fix

- `PluginManager.MaxPendingIncomingChats` 256 → 4096.
- Dropped lines are counted and logged at most every 10 s
  (`PluginManager: N incoming chat line(s) dropped before plugins heard them`).

## Deploy

Engine change: install with all AC clients closed (or relog after the installer). Don't
hot-copy `RynthCore.Engine.dll` into a running `Runtime\`.

# 2026.10.16.21 — Full install ships the RynthSuite companion plugins

**Kind:** Hot push (test build installer). Previous live tip `2026.10.16.20` (Engine + Oracle 0.3.3).

## Problem

The installer packaged only RynthAi and the six experimental plugins. RynthOracle,
RynthILT, RynthInventory and RynthSort — where most of today's fixes live (quest refresh,
aug Refresh current, Fellowship `/fship`) — were never in a full install; they only arrived
through the update feed or a hand copy. RynthAi's DLL also read `1.0.0` (published without
the release version).

## Change

- New installer component group **RynthSuite: Companion plugins** (on in a Full install):
  RynthOracle, RynthILT, RynthInventory, RynthSort. Each installs to
  `<RynthSuite>\<Name>\RynthCore.Plugin.<Name>.dll` and is registered with the launcher
  like RynthAi.
- `Build-Installer.ps1` publishes them (NativeAOT) with the release calver, stages and
  checks them, and lists them in `release-manifest.json`.
- RynthAi publishes with the release calver too (its plugin version stays
  `HandWrittenVersion`).

## Includes

Engine `2026.10.16.20` chat queue (4096) and RynthOracle 0.3.3 quest refresh fix, on the
`test/hotbuild-2026-10-16-on-aelrynth` merge (current `aelrynth/main`).

# 2026.10.16.19 — Updater: local install ahead of published feed is not an error

**Kind:** Hot push (test branch + on-aelrynth merge rebuild).

## Problem

After installing a local test build (`2026.10.16.18`) newer than the public update
feed (`2026.10.15.7`), **Check for updates** showed **Couldn't check for updates**:
`the feed is older (2026101507) than one already seen (2026101618)`.

## Cause

The replay floor used `max(highestSeenFeed, InstalledCoreRelease)`. A local/test
`release.txt` ahead of the signed feed made every legitimate older feed look like a
replay attack.

## Fix

Replay guard uses only the highest *feed* already accepted. A local install ahead of
the published feed still checks successfully (no core update offered).

## Also

Installer rebuilt from `test/hotbuild-*-on-aelrynth` (test branch merged onto current
`aelrynth/main`), not from the divergent daily test tip alone.

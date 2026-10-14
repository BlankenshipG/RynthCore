# 2026.10.5.57 - attacks and target selects no longer wait behind gestures

Previous release: 2026.10.5.56.

Goes with RynthAi 0.5.64-legacy-ui (fewer weapon swaps, faster point-blank
stall recovery: `RynthSuite/Changelog/2026-10-14-combat-speed-swaps-stalls-0.5.64.md`).

## Problem

While a gesture is in flight (the CMotionInterp pending-motion list is not
empty), `AcMainThreadQueue` defers the whole action ring for up to
`DrainDeferMaxMs` (2.5 s). With a crossbow or bow, a gesture is in flight
nearly all the time. In Silentkelpie's 2026-10-14 logs, target selects and
attacks ran 1.0-1.9 s late, and some attacks went past the 1.5 s stale limit
and were dropped.

## Change

- New `AcMainThreadQueue.RunsDuringGesture`. The retail client does these from
  a key press at any moment, swing or not, so they now run during the defer:
  - `SetSelectedObject` (the target select).
  - `NativeAttack` (ClientCombatSystem StartAttackRequest / EndAttackRequest,
    which the client itself holds until the motion allows it).
  - `CancelAttack` (the 0x1B7 packet).
- These run from the head of the ring up to the first entry that doesn't
  qualify, using `ActionRing.TryPeek` and `Pop`. FIFO order is kept: nothing
  overtakes a held entry.
- Stance changes, movement, raw attack packets and item moves stay deferred as
  before. When the gesture-safe entries empty the ring, the defer clock resets,
  so the next held entry gets its full defer.

## Tests

- Engine builds. All RynthCore tool tests pass (EngineRobustnessTests 1159
  checks, among others). There is no unit harness for the drain (it needs AC's main
  thread). The change is checked live through RynthAi's logs: attacks should no
  longer be reported late or stale behind crossbow gestures.

## Files

- `src/RynthCore.Engine/Compatibility/AcMainThreadQueue.cs`

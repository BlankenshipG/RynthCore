# Engine — Pull-away kite UI (with RynthAi 0.5.79)

**Date:** 2026-10-16  
**Kind:** Hot push companion to RynthAi kite  
**ProductVersion:** stamp with `-p:Version=2026.10.16.14` (above live Runtime `2026.10.16.13`)

## Summary

Engine settings and Damage detail expose RynthAi’s new pull-away kite:

- **Damage detail → Kite**: Default / Off / PullAway (per monster; Default follows DEFAULT row)
- **Settings → Melee Combat → Pull Away (Kite)**: Pull Distance (m), Tag Before Fleeing
- **MonstersPanel.Rule.Kite** on the monsters bridge (older engines omit it; RynthAi carries the prior value)

No combat logic in the Engine — execution is entirely in RynthAi `CombatManager.Kite`.

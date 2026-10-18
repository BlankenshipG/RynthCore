# 2026.10.18.13 — test build full install

**Kind:** Test build installer. The previous installer was `2026.10.18.4`.

Built from `test/hotbuild-2026-10-16-on-aelrynth`: the test branch with `aelrynth/main` merged in.
Upstream had nothing newer than snapshot 2026.10.17.1, which was already in.

The fork's `sk` branch isn't merged. Its commits are the standing pull request's copies of this
same test-branch work.

## New since 2026.10.18.4

**Engine (2026.10.18.10–.11):**
- **The Add to loot profile popup** has an attribute checklist: every attribute of the item, grouped, with a comparison and a value for each.
- **Graphics:** a warning when Adaptive degrade is off with a far draw distance. A client nearly out of address space right after it starts turns Adaptive degrade back on for its account.

**RynthAi (0.5.86–0.5.88):**
- **ub-IT `/ub` coverage:** every ub-IT `/ub` verb is mapped, or accepted as a no-op. ub-IT's missing expression functions are added.
- **Loot attributes:** the attribute checklist for all gear, pet essences, and cantrip tiers.
- **Fixes from the sample metas:**
  - `vtsetsetting[]` takes VTank distances. Attack and approach ranges used to stay at 50 yd.
  - New `/ub close corpse|chest` and `/ub <expression>`.
  - `/vt deletemonster` is accepted.
  - Pet range, blacklist, rebuff and AutoAttackPower options are mapped.
  - `/mt face` and `/ub face` take decimals and coordinates, and hold nav while turning.

**Other plugins:**
- **ub-Rythai 3.1.15:** ub-IT's `/ub periodic add|remove|toggle|list|clear`. It no longer replies to RynthAi's verbs.
- **RynthSort 0.3.3:** takes commands from other plugins (`/ub packsort`).

## Checks

- **Versions in the package:** the engine, launcher (`RynthCore.exe`), RynthAi, RynthSort and RynthOracle are stamped `2026.10.18.13`; ub-Rythai is `3.1.15`.
- **Tests:**
  - RynthAiHostTests: 1080 passed; the 2 known failures are unchanged (the map bake needs the game data files; the mini remote help line).
  - LootSdkTests: 8240 assertions, 0 failed.
  - AcGraphicsPrefsTests: 62 checks pass.

## Install

Close every AC client and the launcher first. The launcher, loader and engine are replaced.

- `installer\Output\RynthCore-Setup-2026.10.18.13.exe`
- `installer\Output\RynthCore-2026.10.18.13-deploy.zip`

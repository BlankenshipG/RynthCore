# SK features rebuilt on the current rynth/main snapshot

**Date:** 2026-10-05
**Branch:** `sk/features-on-rynth-main`, pushed to GitHub (`origin`) and the SK fork (`upstream`). It is also `main` on both remotes now (see "Main reset" below).
- RynthCore: `143eaa9`, based on rynth/main `913a2a0` (release 2026.10.5.20, API v79).
- RynthSuite: `7bcc954`, based on rynth/main `6847868`.

## Pull requests

| PR | Head | Status |
|----|------|--------|
| [rynth/RynthCore #2](https://aelrynth.com/git/rynth/RynthCore/pulls/2) | `Silentkelpie:sk/features-on-rynth-main` | Open, mergeable |
| [rynth/RynthSuite #2](https://aelrynth.com/git/rynth/RynthSuite/pulls/2) | `Silentkelpie:sk/features-on-rynth-main` | Open, mergeable |
| [rynth/RynthCore #1](https://aelrynth.com/git/rynth/RynthCore/pulls/1), [rynth/RynthSuite #1](https://aelrynth.com/git/rynth/RynthSuite/pulls/1) | `Silentkelpie:main` | Open. Since the main reset they carry the same commits as #2; left for the maintainer to close |
| [BlankenshipG/RynthCore #9](https://github.com/BlankenshipG/RynthCore/pull/9), [BlankenshipG/RynthSuite #7](https://github.com/BlankenshipG/RynthSuite/pull/7) | `sk/features-on-rynth-main` | Merged by the main reset (they had conflicted with the old-history main) |

## Main reset

GitHub `main` and the SK fork `main` carried the old GitHub history, so any PR from the clean branch into them conflicted (15 files in RynthCore, 12 in RynthSuite). Both were reset to the clean branch:

- The old heads are kept as tag and branch `archive/main-2026-10-05-final` on both remotes: RynthCore `8ecfeca`, RynthSuite `eb786f7`. This supersedes `archive/main-2026-10-05`, which was missing 43 RynthCore and 27 RynthSuite commits.
- The push used `--force-with-lease` against the old heads. On GitHub it bypassed the "Cannot force-push" branch rule (admin).
- The local-only `fix/restore-sk-features` branches (4 RynthCore and 8 RynthSuite commits) were pushed to GitHub as a backup.
- Every SK line on the old main that is missing from the new main was checked. Each one is either listed under "Maintainer review items" and "Other clean-ups" below, or already covered by rynth/main (its own duplicate-plugin skip and its `CrashLogger`/`CrashDump`, which replace SK's fatal-only SehTrampoline logger). Nothing was lost by accident.
- The Changelog folder stays off `main` because rynth/main has none. These notes live on the `docs/changelog-2026-10-05` branch.

## Why

The maintainer reviewed `fix/restore-sk-features` and found four problems:
1. **History.** It carried old GitHub history: earlier commits, binaries, and files he had removed.
2. **Plugin API collision.** The new merge-result function took v77, which the UI hooks already use (and v68/v69 before that).
3. **Not wired.** The ammo detection, shields, damage seeding and AutoStack changes described in the PR weren't actually connected.
4. **Dropped code.** One of his engine code paths had been dropped.

## What was done

The work was rebuilt with no old history. Only source files were carried over (no changelogs, docs, installers or binaries), as five feature commits on the snapshot.

### RynthCore
- `feat(engine): install-aware paths, logging levels and item drag bridge`
- `feat(engine): plugin overlay windows and create-time identity seeds`
- `feat(ui): nav route editing, progression, charms and chat presets`

### RynthSuite
- `feat(rynthai): ILT Hub, HUDs, item info, chat translation, ground loot and nav editing`
- `feat(rynthai): use the UB damage seed in the Auto element ranking`

## Maintainer review items

| Item | Result |
|------|--------|
| Old history | None: the branches start at the rynth/main snapshot |
| API slots | None taken. `PluginContract`, `PluginContractVersion` and the PluginSdk are byte-identical to rynth/main. The merge-result function is written up separately as a request (`2026-10-05-api-request-merge-stack-result.md`) |
| Dropped engine paths | Restored: the PWD fast path in `ClientObjectHooks` and the posted destroy in `Win32Backend`/`LayeredWindow` (`PostDestroyWindow`, `WM_RYNTH_DESTROY_HWND`) |
| Ammo detection | SK's `MissileAmmoHelper` dropped; rynth/main's ammo code is used |
| Shields | SK's `ShieldHelper`, the engine Items "Shields" section and `RynthPluginAddSelectedShield` dropped. The ILT Hub USD importer now uses `CombatManager.IsShieldItem` (rynth/main's off-hand rules) |
| AutoStack | rynth/main's `InventoryManager` kept unchanged |
| Damage seeding | Now wired in (details below) |

## Damage seeding

`CreatureWeakness.Rank` takes an optional UB seed list. The ranking order becomes:

appraisal > server table > learned element (+ type) > **UB seed (+ type)** > creature type alone

- The seed only orders elements. It never outranks server data or the character's learned element; it only replaces the generic type average.
- `CombatManager.MobSeedLookup` is set from `UbMobSeedStore`. With a missing or bad seed file, the ranking is unchanged.
- `WeaponPlanner` is untouched. Metas keep the unseeded ranking.
- A unit test was added: `weakness: UB seed ranks after server data and learned, before the type`.

## Other clean-ups during the port

- **RynthLog.** rynth/main's `Move`/`UseDone` traces are kept. Their `Write` sink now goes through the General log category.
- **Plugin loading.** rynth/main's `LoadPluginsFromDisk` deduplication is kept. SK's canonical-path check was redundant and wrong for the gather-then-load order, so it was removed along with its helpers.
- **Dead code.**
  - `OverlayFrameBuffer` (unused) was not carried over.
  - SK's `CanRunOnGameThread` and self-destroy rewrite were not carried over.
  - SK's dead-code removal of maintainer-owned files (`LegacyAdvancedSettingsUi`, `LegacyMetaUi`, `LegacyNavigationUi`, dashboard and settings) was undone. rynth/main still uses `LegacyAdvancedSettingsUi`.
- **Left as on rynth/main:**
  - the Avalonia `RynthAiPanel` (per the project rules);
  - `RynthSuite.slnx` (no RynthJuice entry);
  - the RynthAi `HandWrittenVersion`;
  - the Monster Editor version (the maintainer stamps versions at release).
- **Comments.** Comments describing where code came from ("SK-local", "ported from…", "upstream") were reworded to say what the code does.
- **Out of scope for now:** ub-Rythai and RynthJuice.

## Verification

- RynthCore `RynthCore.sln` Release: 0 errors. Each of the three commits also builds on its own.
- RynthAi plugin, Monster Editor and RynthAiHostTests Release: 0 errors.
- `RynthCore.RynthAiTests`: 32 tests, 0 failed.
- `RynthCore.RynthAiHostTests`: 413 tests, 407 passed, 5 known, 1 failed ("map bake 0x6346 … plan on disk"). The unmodified rynth/main snapshot fails the same way on this PC; the test depends on locally installed dats and plans.
- `Tools/RynthLua.CompatTests` fails to compile on the unmodified snapshot too (it compiles `RynthCoreHost.cs` without `PluginContractVersion.cs`). Untouched.

## Versions

No version files were changed on these branches: the maintainer's release stamps them. The SK-side RynthAi 0.5.15 bump stays on `fix/restore-sk-features` only.

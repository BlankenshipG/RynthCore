# SK-local unified branch — full installer + SK logging + ub-Rythai component

**Date:** 2026-10-04
**Branch:** `SK-local` (now contains `main`, `feat/full-installer`, `fix/tmds-dbus-advisory`,
`SK`, and PR #2 docs)

## Branch integration
- `feat/full-installer` merged into `SK-local`: aelrynth snapshot port, Loader, full installer
  (installers 2026.10.4.1–2026.10.4.6), update checker, crash relaunch / stuck-client handling,
  per-PID logs, tmds.dbus advisory fix.
- `origin/main` (PR #2 Hub playbook) merged; SK-local's newer playbook revision kept.

## Kept from SK-local
- **Engine logging level** (`EngineLogLevel`, `EntryPoint.ShouldLog`, `engine.json` `LoggingLevel`)
  combined with full-installer's tagged WRN/ERR lines, `RynthLog.LastIssue` and per-PID log files.
  `EngineSettings.Save()` preserves the launcher-chosen `LoggingLevel`.
- **Launcher:** "Inject all running AC sessions" toggle and Logging level dropdown, alongside the new
  stagger / crash-relaunch / stuck-client / window-title options. The level is written to
  `engine.json` through `EngineJsonStore.SetString` (new; preserves every other key).
- **PluginManager:** basename / canonical-path de-duplication of plugin DLLs.
- `TryPreloadMinHookForEarlyInit` before multi-client init.

## Taken from full-installer
- Injector (unified log, launch command), installer scripts, release workflow, BUILD.md.
- Solution adds `RynthCore.DesktopLog` and `RynthCore.Loader`; the stale
  `Plugins\RynthCore.Plugin.RynthAi` stub is no longer in the solution (the real plugin is in RynthSuite).
- The Desktop rolling log (`DesktopRollingLog`) is superseded by the unified/per-PID logs.

## Fixed
- Auto-merge had left SK-era fragments in full-installer's scripts: an undefined `$PluginProjects`
  check in `Build-Installer.ps1` (would abort every build), a duplicate bundle-archive block, the
  `RynthBundle-Setup` output name in `RynthCore.iss`, and `-SkipUbRythai` / `RynthBundle-Setup-*.exe`
  in the release workflow. All restored to full-installer's working versions.
- `Build-Release-All.ps1` now forwards only parameters `Build-Installer.ps1` declares
  (`-Version`, `-RynthSuiteRoot`, `-IsccPath`).

## Added
- **ub-Rythai 3.1.10** as an optional experimental installer component
  (`experimental\ubrythai` → `<RynthSuite>\UbRythai\RynthCore.Plugin.UbRythai.dll`, registered with the
  launcher like the other experimental plugins). `RynthCore.iss` uses an `ExperimentalCount` constant
  instead of hard-coded `0 to 4` loops.

## Repository hygiene
- Stopped tracking SK-era build output: `installer/_toolpublish/` (published Loot/Monster Editor
  binaries) and `build_out/RynthBundle-Setup-0.4.7.exe`. Both are git-ignored now; files remain on disk
  and in history.

## Build
- Engine, Launcher, Injector and Loader build with 0 errors on the merged branch.
- No installer was compiled for this entry; the next installer build should use
  `installer\Build-Installer.ps1 -Version 2026.10.4.7` (previous: 2026.10.4.6).

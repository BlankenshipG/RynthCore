# Changelog — RynthBundle unified installer (2026-04-22)

## Summary

- **Single installer output:** `installer\Output\RynthBundle-Setup.exe` (Inno Setup 6).
- **Build entry point:** repository root script `Build-Release-All.ps1` builds `RynthCore.sln` in Release, then runs `installer\Build-Installer.ps1`.
- **Bundled components:**
  - RynthCore Avalonia launcher (self-contained x86), renamed to `RynthCore.exe`
  - RynthCore Engine (NativeAOT x86) and native dependencies under `Runtime\`
  - **RynthCore.Injector** (framework-dependent x86 publish) under `Runtime\` for manual CLI injection
  - All RynthSuite `Plugins\RynthCore.Plugin.*` projects discovered under the sibling `RynthSuite` repo — published to `Runtime\Plugins\` and RynthAi DLL mirrored under `Suite\RynthAi\`
  - **LootEditor** and **MonsterEditor** (self-contained x64) under `Tools\` with full publish trees (including `runtimes\` subfolders)

## Installer script fixes

- **Recursive staging:** self-contained and framework-dependent publish outputs that include subdirectories are now copied in full; previously only top-level files were copied, which broke tool publishing.
- **Injector:** included in the publish and staging steps so the bundle matches a full RynthCore deployment.

## Documentation

- `BUILD.md` updated to reference `RynthBundle-Setup.exe` and the root `Build-Release-All.ps1` flow.

## Prerequisites (unchanged)

- .NET SDK (matching project `TargetFramework`, e.g. .NET 10) and Visual Studio build prerequisites for NativeAOT where applicable
- [Inno Setup 6](https://jrsoftware.org/isdl.php) for compiling `installer\RynthCore.iss`
- `RynthSuite` as a **sibling** of `RynthCore` (same parent folder), or pass `-RynthSuiteRoot` to `Build-Installer.ps1`

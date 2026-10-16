# Changelog — Rynth bundle installer: .NET x64 prereq advisory only (2026-04-23)

## Summary

**`installer/RynthCore.iss`:** The post-Welcome **.NET 10+ (x64)** page for Loot/Monster tools is now **informational only**. **Next** stays enabled regardless of registry detection; copy and install always proceed. Copy and button text clarify that **`RynthCore.exe` / injector** remain self-contained.

## Scope

This policy applies to the **Rynth client / bundle installer** only. **UB-ILT / UBLoader** retains its own **UtilityBelt** net10 registry gate in **`FilterCore.LoadPluginAssembly`** (unchanged).

## Related

- **`RynthCore.DesktopLog.NetDesktopX64Prerequisite`** — unchanged; still mirrors the same registry paths for launcher log / activity alignment.

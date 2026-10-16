# Changelog — 2026-10-04 (upstream snapshot)

## feat: port aelrynth git snapshot onto GitHub history

Applied `upstream/main` (`9b5e276`, RynthCore as of 2026-09-27, release 2026.9.27.2) as a new commit on top of BlankenshipG `main`. Histories are unrelated, so this is a tree overlay — not a fast-forward or force-replace.

- Added aelrynth-only product code: Loader, StatusAgent, HookManifest/HookResolver, DComp overlay, SEH trampoline, signed updater, vendor/trade API (PluginContract +16 functions)
- Overlapping engine/launcher files taken from aelrynth (newer than origin 2026-04-25)
- Kept unique origin-only trees: `Plugins/RynthAi/` (Decal-era), `Plugins/RynthCore.Plugin.RynthAi/` stub, `AGENTS.md`/`CLAUDE.md`, `ImGuiController.cs`, `OverlayFrameBuffer.cs`
- Re-applied dead-link fixes: installer + CI clone URLs point at aelrynth.com / `${{ github.repository_owner }}` siblings

No PluginSdk version bump (still 0.2.0 on both trees). Packaged aelrynth installer (2026.10.4.3) is newer than this git tip.

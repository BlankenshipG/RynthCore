## RynthCore Installer Update - Full RynthCore + RynthSuite Setup

Date: 2026-04-22

### What changed
- Expanded `installer/Build-Installer.ps1` to package a full setup payload:
  - Publishes launcher + engine.
  - Discovers and publishes all default `RynthSuite` plugin projects (`RynthCore.Plugin.*`).
  - Bundles `RynthCore.LootEditor` and `RynthCore.MonsterEditor` as self-contained tools.
- Updated installer staging to copy default plugin DLLs into `Runtime/Plugins` for automatic loading.
- ~~Added a mirrored `RynthAi` plugin copy under `Suite/RynthAi/` for data/profile adjacency.~~ **Removed in 0.4.8** — plugin lives only in `Runtime\Plugins\`; data stays under `C:\Games\RynthSuite\RynthAi\`.
- Updated `installer/RynthCore.iss`:
  - Added `MonsterProfiles` data directory creation.
  - Added Start Menu shortcuts for Loot Editor and Monster Editor when present.
- Updated `BUILD.md` installer documentation to reflect full setup behavior and default plugin autoload.

### Why
- Provide one setup executable that installs both core runtime and suite defaults.
- Remove manual post-install steps for loading default plugins and installing suite tools.

# Test build 2026.10.18.24: full installer (2026-10-18)

Previous installer: 2026.10.18.13. Previous engine hot build: 2026.10.18.15.

Built from the local test merge `test/hotbuild-2026-10-16-on-aelrynth` in both repos. Upstream
(aelrynth `main`) was fetched first and has nothing new: it is still the 2026.10.17.1 release
snapshot (RynthCore `8330793`, RynthSuite `c7807b6`), which both merges already contain.

## Stamps

- **Launcher, engine, Loot Editor, Monster Editor:** 2026.10.18.24.
- **RynthAi, RynthOracle, RynthILT, RynthInventory, RynthSort:** 2026.10.18.24.
- **RynthNav, RynthChat, RynthTracker, RynthVision:** 2026.10.18.24. They used to ship as 1.0.0 (see Fixed below).
- **Keep their own versions:** RynthJuice 0.1.3-experimental, UbRythai 3.1.15.
- **Plugin versions:**
  - RynthOracle 0.6.0
  - RynthILT 0.2.20
  - RynthNav 0.6.8
  - RynthAi 0.5.88-legacy-ui

## Fixed: installer stamps

`installer/Build-Installer.ps1` published every experimental plugin without `-p:Version`, on the
grounds that each "keeps its own semantic version". Only RynthJuice and UbRythai set a `<Version>`,
so RynthNav, RynthChat, RynthTracker and RynthVision shipped as 1.0.0.

Now `Test-OwnVersion` checks each plugin's csproj:
- a plugin that sets its own `<Version>` keeps it;
- every other plugin gets the release calver, like the companion plugins.

## In this installer since 2026.10.18.13

**RynthCore engine**
- 2026.10.18.14: the Aelrynth Vault button in the Classic inventory.
- 2026.10.18.15: the Aelrynth Vault in the Modern inventory.

**RynthOracle 0.5.1 to 0.6.0**
- Dynamic Stamps route through RynthNav.
- A carried Parchment can be given to a stamp's NPC.
- Quests get Go to for coordinates in their notes.
- The quest trackers refresh like the Quests tab.
- **0.6.0:** the Legendary, Markers and Dynamic Stamps tabs and the Temple guardians are merged
  into the Quests list:
  - rows by server, with a "Trackers only" filter;
  - RynthNav Go to on every kind of row;
  - guardian auto hand-in and buy-if-missing controls.

**RynthILT 0.2.17 to 0.2.20**
- Auto-chunk rules match ub-IT:
  - unlevelled Coalesced Aetheria are chunked;
  - two-handed weapons with cleave 2 are chunked;
  - chunking can run while the client is busy in combat;
  - corrupted-weapon keep rules.
- A rating-level threshold for aetheria chunking.
- A chunked item that leaves the pack counts as chunked.
- "RynthILT.Data" version 3 carries the Temple guardian data for Oracle.

**RynthNav 0.6.8**
- `a || b || c` alternatives: RynthNav takes the first that is on its map.

The details are in each RynthSuite `Changelog/2026-10-18-*.md` file.

## Outputs

- `installer/Output/RynthCore-Setup-2026.10.18.24.exe`
- `installer/Output/RynthCore-2026.10.18.24-deploy.zip`

Close every AC client and the launcher before installing.

# RynthCore: installer text for RynthILT's fellowship commands

Branch `feat/dynamic-sections`, after 2026.10.5.48. No engine or launcher change, so no new
release number. The text goes out with the next installer build.

`installer/Publish-Update.ps1`: the RynthILT line in the README and its feed description now
mention the `/fship` fellowship commands. RynthILT 0.1.3 adds them to the Mini Remote's Fellowship
section (see RynthSuite `Changelog/2026-10-13-fellowship-view-ilt-fship.md`).

The Fellowship section itself needs no change: RynthILT's pane comes through the sections
manifest that 2026.10.5.46 added (a `fellowship` contribution, server `ilt`).

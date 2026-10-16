# 2026.10.5.54 - Mini Remote "Session Stats", moved tabs, one session formula

Previous release: 2026.10.5.53.

Goes with RynthSuite's "Mini Remote tabs moved, one XP / luminance session"
release (`RynthSuite/Changelog/2026-10-13-oracle-character-global-banking-one-session.md`):
RynthAi 0.5.62-legacy-ui, RynthILT 0.2.6, RynthOracle 0.2.5, RynthInventory
0.1.2, RynthTracker 0.4.1, ub-Rythai 3.1.13 and the 16 server plugins at 0.1.2.

## Problem

- The Mini Remote's "Char" section mixed several things: session rates, the ILT
  character page, charms, banking and the server-force checkboxes.
- The engine's XP / luminance rates (status feed, Mini Remote overview) used
  their own rule, a per-process baseline that reset on an XP drop. That rule
  didn't match RynthAi's or RynthTracker's numbers on ACECustom (bank 9005) or
  Aelrynth (9102 / 9103).

## Change

- The "char" section is now labelled "Session Stats". Its id stays "char", so
  saved rail picks and `/rc miniremote section char` still work.
- If a plugin contributes a primary view to "char" for every world (RynthAi's
  "section:session"), the engine drops its own overview there, so only one
  session shows. A server-scoped view doesn't do this.
- `/rc miniremote tab character|char|charms` now runs `/ro character`
  (RynthOracle's Character tab). `/rc miniremote tab banking|bank` now runs
  `/ginv bank` (RynthInventory's Banking tab). The ILT tabs left are Gear and
  Games.
- New `/rc diagnostics` (alias `serverpick`) opens Options on the Diagnostics
  tab. This is now the only place to force a server; the plugins link to it.
- New `Compatibility/SessionRates.cs` (pure) uses the same formula as
  RynthSuite's `Plugins/Shared/SessionStats.cs`:
  - Totals are the sum of positive rises.
  - Bank 9005 rises count as income. Once 9005 has been read, AvailableLuminance
    rises are treated as withdrawals.
  - A deposit or incoming transfer seen in chat is left out of the next bank
    rise.
  - 9102 is added and 9103 is subtracted.
  - A different character starts a new session.
  `EngineStatusMetrics` (GetEngineStatusJson) and the Mini Remote's own overview
  use it. The player quad properties the engine caches now include 9005, and
  PluginManager passes chat lines to it.
- On the RynthAi panel, the Char button and the "Character" menu item open
  RynthOracle's Character tab. Without RynthOracle they open the Mini Remote's
  Session Stats section.

## Tests

- tools/SectionsTests: 140/140. New cases cover the Session Stats label, a
  universal session surface dropping the overview, and a server-scoped one
  keeping it. New SessionRates cases cover rises and drops, the ACECustom bank,
  deposits and transfers seen in chat, Aelrynth 9102 / 9103, and a new
  character.

## Install

Installed locally on 2026-10-13 with RynthCore-Setup-2026.10.5.54.exe
(silent; RynthCore 3405fb7, RynthSuite a316298). The installer brought RynthAi
0.5.62-legacy-ui, RynthTracker 0.4.1 and ub-Rythai 3.1.13. RynthILT 0.2.6,
RynthOracle 0.2.5, RynthInventory 0.1.2 and the 16 server plugins at 0.1.2 were
copied from their NativeAOT `publish` builds. Old DLLs were backed up to each
plugin's PreviousReleases folder, named by the version they replaced.

## Files

- `src/RynthCore.Engine/Compatibility/SessionRates.cs` (new)
- `src/RynthCore.Engine/Compatibility/EngineStatusMetrics.cs`, `ClientObjectHooks.cs`
- `src/RynthCore.Engine/Compatibility/RynthCoreChatCommands.cs`
- `src/RynthCore.Engine/Plugins/PluginManager.cs`, `EntryPoint.cs`
- `src/RynthCore.Engine/ImGui/Panels/MiniRemoteFace.cs`, `MiniRemoteFace.Sections.cs`, `RynthAiFace.cs`
- `src/RynthCore.Engine/UI/Sections/SectionCatalog.cs`
- `tools/SectionsTests/Program.cs`, `SectionsTests.csproj`

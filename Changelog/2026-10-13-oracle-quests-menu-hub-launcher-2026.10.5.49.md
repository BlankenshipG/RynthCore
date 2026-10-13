# RynthCore 2026.10.5.49: Char menu opens RynthOracle's quests; Hub launcher removed

Local full install. Previous release: 2026.10.5.48.

## What changed

- **RynthAi dashboard: Hub launcher removed.** The Hub button opened the same Mini Remote window as
  Char, so it is gone. The launcher row has 9 buttons. The Mini Remote is still the bar button
  (`/rc miniremote`), Char (on its Character tab), and "Mini Remote" in Char's right-click menu;
  its drawer is the drawer tab on the dashboard's edge. The Avalonia copy of the dashboard
  (`UI/Panels/RynthAiPanel.cs`) loses its Hub button too, and its right-click (Inventory HUDs
  setup, `/ra huds`) moves to Char.
- **Char menu: Quests are RynthOracle's.** "Quests" now opens or closes RynthOracle's window
  (`RynthOracle/Oracle`) instead of the Mini Remote's Quests tab, which moved to RynthOracle. A
  new "Quest Timers" item opens its Quest Timers window (`RynthOracle/QuestTimers`). Both are
  ticked while open and greyed out when RynthOracle isn't loaded.
- **Script windows: open by key.** `ScriptWindowRegistry.PlayerSetVisible(string key, bool)`,
  `Has(key)` and `IsOpen(key)` find a plugin's script window by its key and show or hide it as the
  bar does: the owner gets a Visibility event with reason 2.

## Files

- `src/RynthCore.Engine/ImGui/Panels/RynthAiFace.cs`: 9 launchers, `OracleMenuItem`, Char menu.
- `src/RynthCore.Engine/UI/Panels/RynthAiPanel.cs`: Hub button removed, its right-click on Char.
- `src/RynthCore.Engine/UI/ScriptWindows/ScriptWindowRegistry.cs`: key lookups.
- `src/RynthCore.Engine/Compatibility/RynthCoreChatCommands.cs`: comment (tab list).

## Build and install

- `installer\Output\RynthCore-Setup-2026.10.5.49.exe` and `RynthCore-2026.10.5.49-deploy.zip`
  (125.8 MB, verified). The 2026.10.5.48 installer is archived in `installer\previous-release\`.
- Installed silently on this machine. RynthOracle and RynthILT aren't in the installer and were
  copied in directly. The replaced plugin DLLs are in each plugin's `PreviousReleases\<version>`.

## Also in this install (RynthSuite)

RynthOracle 0.2.2 (quests merged in, rewards, Quest Timers window), RynthAi 0.5.57-legacy-ui
(Mini Remote Quests tab removed), RynthILT 0.2.3 (`/qb` list for RynthOracle). See RynthSuite
`Changelog/2026-10-13-quests-to-oracle-rewards-timers.md`.

Not yet tested in game.

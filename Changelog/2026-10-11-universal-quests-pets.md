# Char menu: Quests and Pets under Character; "Server: <name>" for the server section

## What changed

- **Quests and Pets are universal.** RynthAi's Mini Remote shows those tabs on every server, so
  they moved:
  - into the Char right-click menu's **Character** half, after Spells. They open the Mini Remote
    window on that tab (`/rc miniremote tab quests|pets`);
  - out of the Infinite Leaftide server registration (`EntryPoint.RegisterServerMenus`).
- **Guardian stays ILT only**, in the server section, together with:
  - the Character, Banking, Gear and Games tabs;
  - Bounties (RynthILT);
  - Progression.
- **Server section header:** each active section is now titled "Server: <name>", e.g. "Server:
  Infinite Leaftide" (`ServerMenus.DrawImGuiMenu`). RynthAi's Mini Remote menu uses the same
  header.
- **Char launcher tooltip** now lists Quests and Pets.

## Files

- `src/RynthCore.Engine/EntryPoint.cs`: the ILT tab list without Quests and Pets.
- `src/RynthCore.Engine/UI/ServerMenus.cs`: the "Server: " header.
- `src/RynthCore.Engine/ImGui/Panels/RynthAiFace.cs`: Quests and Pets in `CharMenu`; the
  tooltip; the `ShowMiniRemoteTab` doc.

## Testing

- The engine builds in Release, and ItemHudTests pass.
- Not yet checked in game.

## Installed

- 2026-10-11 22:08: published to `C:\Games\RynthCore\Runtime`. Both running clients (pids 11468
  and 61016) hot-reloaded to it (gen 3) without errors.
- 2026.10.5.30 is saved in `C:\Games\Rynthcore\PreviousReleases\2026.10.5.30-engine` and
  `C:\Games\_rynth-backups\20261011-pre-universal-tabs\Runtime`.

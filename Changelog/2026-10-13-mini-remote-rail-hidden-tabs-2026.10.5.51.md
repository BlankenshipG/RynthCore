# RynthCore 2026.10.5.51: ILT tabs come back when the sections rail is hidden

| Component | Previous | Now |
|---|---|---|
| Engine | 2026.10.5.50 | 2026.10.5.51 |
| Engine plugin API | 81 | 81 (unchanged) |

## The problem

After 2026.10.5.50, Infinite Leaftide's Banking tab and the ILT settings view could not be
found in the Mini Remote.

When RynthAi publishes sections, the engine asked RynthAi for its remote without its tab bar
("miniremote-remote"), because the sections rail replaces that bar. RynthILT's tabs (Character,
Banking, Gear, Charms, Games) then live only in the Char, Inventory and Games sections. ILT
settings lives only in the Char section's "Server: Infinite Leaftide" tab.

The rail can be hidden (right-click menu, "Show the sections rail"), and that choice is saved for
the whole PC (`panel_columns.txt`, `MiniRemote.rail`). With the rail hidden, those tabs and
sections had no visible way in. The only routes were the right-click menu and
`/rc miniremote section <name>`.

## The fix

- `MiniRemoteFace.Surface` asks for "miniremote-remote" only while the rail shows. With the rail
  hidden it asks for "miniremote-window", so RynthAi's remote draws its own tab bar again,
  including RynthILT's Banking, Gear, Charms, Character and Games tabs.
- The "Show the sections rail" tooltip says what hiding the rail does.

## Where things are

- **Banking** is in the Inventory section's Banking tab, or in RynthAi's tab bar when the rail is
  hidden. `/rc miniremote tab banking` opens it either way.
- **ILT settings** is in the Char section's "Server: Infinite Leaftide" tab, which shows only
  while RynthCore treats the world as ILT. `/ilt settings` opens RynthILT's own window.

## Files

- `src/RynthCore.Engine/ImGui/Panels/MiniRemoteFace.cs`: `Surface` checks `RailShown`.
- `src/RynthCore.Engine/ImGui/Panels/MiniRemoteFace.Sections.cs`: the rail tooltip.

## Local install (2026-10-13 20:30)

- `installer\Output\RynthCore-Setup-2026.10.5.51.exe` installed silently to `C:\Games\RynthCore`.
  The 2026.10.5.50 installer is archived in `installer\previous-release\`.
- RynthILT 0.2.5 (RynthSuite `ce91cf4`, the client-side ILT feature profile) was copied to
  `C:\Games\RynthSuite\RynthILT`. 0.2.4 is kept in `PreviousReleases\0.2.4`.

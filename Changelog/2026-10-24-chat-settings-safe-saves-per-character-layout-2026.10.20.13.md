# Engine 2026.10.20.13: chat filters stop getting lost; each character keeps its own window layout

Previous release: 2026.10.20.12, this change's first hot deploy, which was missing a log line.
Before that: 2026.10.20.11.

## Fixed

- Silentkelpie, running five clients: RynthChat "keeps losing" its filters, they "stop working",
  and the chat window resets. The RynthChat plugin wasn't the cause. Every client on the machine
  shares two files, and each wrote its whole state over the other clients':
  - **Chat rules and standard filters** (`%AppData%\RynthCore\rynthchat_settings.json`). A client
    re-read the file only while its chat window was open. A client with chat closed kept old rules
    in memory and wrote them back at its next save, even from a tab click. Every other client then
    reloaded that old copy, and each standard filter the file didn't list went off. On 2026-10-24
    between 08:43 and 08:46 one client saved 15 times and the four others reloaded every one.
  - **Window positions** (`%LocalAppData%\RynthCore\panel_state.txt`, one `panel.Chat` row for
    every client). A client read it at start and on an engine reload, then rewrote all of it on any
    window move, open or close. So a client's chat window came back wherever the client that saved
    last had its own, or closed. The file was also rewritten in place, so a client dying mid-write
    could leave it cut off.

## Changed

- **Chat settings are checked all the time.** The engine's UI step (`UiDataHub`) now checks the
  shared file every step, throttled to a timestamp check every 2 s, whether or not the chat window
  is open. Before, only the chat window's data source did, while it was subscribed.
- **A save merges instead of overwriting** (`UI/Data/ChatSettingsMerge.cs`). When another client
  changed the file since this client's base (what its settings in memory came from), each
  top-level setting is decided on its own:
  - a setting this client changed is saved from this client;
  - a setting this client left alone keeps the file's value;
  - a setting only the other client wrote is kept.
  The client then picks up the merged result itself. A file that can't be read, or a merge that
  doesn't load back, leaves the save as it was. The rule list and the standard filters are each
  one setting, so if two clients edit rules at the same moment, the later save's list still wins.
  The base is taken when the save is asked for, not when it is written, so a reload in between
  can't make older settings look current. A merged result is applied only if no newer save has
  been asked for since.
- **A tab click no longer saves.** The selected tab is this client's own; it is written with the
  client's next real change and ignored by the others, as before.
- **Window layout is per character** (`UI/PanelStatePath.cs`, `UI/PanelStateStore.cs`).
  `panel_state.<server>.<character>.txt` comes from this process's launch context
  (`ServerName`, `TargetCharacter`). A character's first run starts from the shared
  `panel_state.txt`, and its saves then go to its own file. A client started outside the launcher
  still uses the shared file. Saves are written to a temp file and swapped in.
- **The log says which layout file is in use**, at the first save. In 2026.10.20.12 this line was
  written at load, which runs during engine start before the log is up, so it never appeared;
  2026.10.20.13 moves it to the first save.

## Tests

- `tools/ChatFilterTests`: "settings merge: a save never undoes another client's rules" covers:
  - a stale client changing only its font and tab keeps the other client's new rule and preset change;
  - a client editing rules keeps its edit, and the other client's font stays;
  - a setting only the other client wrote is kept, and one this client removed stays removed;
  - no base, no file, a broken file, or nobody else saving all leave this client's text as is.
  1648/1648 checks passed.
- `tools/PanelStateTests` (new): file names from the server and character, with unsafe and
  non-ASCII characters as `_`, long names cut, and case not making a second file. The shared name
  is used when the character is unknown. The launch context is read, including broken, missing
  and non-string values. 16/16 checks passed.
- The engine builds (`-c Release -p:EngineHost=coreclr`) with no new warnings.

## Install

Hot deployed with `scripts\Publish-EngineCoreClr.ps1 -EngineOnly -MsBuildArgs
'-p:Version=2026.10.20.13' -AllowBehind`. `-AllowBehind` is needed because this checkout has no
`status-export` branch for the script's safety check; it holds everything the live
2026.10.20.11 engine was built from. Running clients pick up the new engine by themselves; a
client in combat waits for the fight to end (at most 120 s).

After the 2026.10.20.12 deploy, four characters made their own layout files within two minutes,
each started from the shared layout:
`panel_state.aelrynth.sk.txt`, `panel_state.infiniteleaftide.silentstorm.txt`,
`panel_state.infiniteleaftide.silent_pack.txt` and
`panel_state.infiniteleaftide.slient_hoooooooe.txt`. Silentkelpie makes its own at its first
window save. The shared `panel_state.txt` is no longer written by launcher-started clients.

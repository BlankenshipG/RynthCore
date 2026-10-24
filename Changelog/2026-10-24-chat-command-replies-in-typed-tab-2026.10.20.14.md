# Engine 2026.10.20.14: a command's reply shows in the RynthChat tab you typed it in

Previous release: 2026.10.20.13.

## Fixed

- Silentkelpie: `/fship` and other server commands typed in RynthChat's box "don't show up"
  in RynthChat. AC's own chat showed them. Nothing was lost or filtered:
  - The server's reply to a command is system text (ACE's `ChatMessageType.Broadcast`, type 0),
    so RynthChat files it under System, which shows only in the All and System tabs. The
    seven `[FSHIP]: use /fship ...` lines were in All the whole time.
  - The window was on another tab.

  How this was checked, so the same report can be ruled out quickly next time:
  - This PC's rules and standard filters run through the engine's real router leave the
    `[FSHIP]` lines in All and System.
  - RynthChat's chat log showed all seven lines arriving at 09:42:53.
  - No plugin eats them.

## Changed

- **A command's reply follows you** (`UI/Data/ChatReplyTab.cs`). When you send a slash or
  `@` command from RynthChat's box, the tab you're on is marked for 5 s. The System lines (the
  server's replies) and Rynth lines (plugin replies) that arrive in that time also show in
  that tab, as well as in All and System. The mark is kept on the line itself
  (`ChatLine.ReplyTab`, checked in `ChatModel.LineInTab`), so a re-route pass after a rule edit
  keeps it. A rule that hides the line still hides it everywhere.
- Not for chat: `/say`, `/tell`, `/reply`, `/retell`, `/f`, `/a`, emotes and the channel
  commands already land in Chat or a channel tab. Not for commands typed in All, which shows
  everything. Not for automated lines (a plugin's own polls, the login burst). Not for other
  channels, so combat in those seconds stays where it was.

## Tests

- `tools/ChatFilterTests`: "command replies: shown in the tab the command was typed in" covers:
  - which commands count (`/fship`, `/bank d p 10k`, `@help` do; `/tell`, `/f`, `/a`, plain
    text and a lone `/` don't);
  - System and Rynth replies follow to the tab, and combat lines don't;
  - automated lines aren't copied, and the 5 s window ends;
  - typing in All or System marks nothing extra;
  - a `/tell` marks nothing.
  1665/1665 checks passed.
- The engine and `tools/ChatHistoryBench` build with no new warnings.

## Install

Not hot deployed. Every engine hot reload since at least 2026-10-21 closes the running clients
(see the 2026.10.20.13 notes), so this needs the clients closed first, then
`scripts\Publish-EngineCoreClr.ps1 -EngineOnly -MsBuildArgs '-p:Version=2026.10.20.14' -AllowBehind`.

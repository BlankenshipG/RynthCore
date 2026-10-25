# Engine 2026.10.18.30: automated system text shows in the System tab only (2026-10-19)

Previous release: 2026.10.18.29 (incoming chat queue for 25,000-line quest replies). Goes with
RynthChat 0.3.4.

## Why

RynthChat's All tab filled up with system text nobody typed or asked for: the server's login
messages, the `/myquests` and `/qb list` replies the plugins request on their own (thousands of
lines on an Infinite Leaftide character), and the login probes' replies. RynthChat records every
line, including the ones a plugin hides from the retail chat, so those showed under All too.

## Changed

- **Automated system text shows in System only.** A line RynthChat marks automated (`"auto"`)
  and that no rule or standard filter placed is routed to the System tab and left out of All and
  its own channel tab. RynthChat marks:
  - `/myquests` and `/qb list` lines, whoever sent the command;
  - the login burst: system text from entering the world until 30 s after login completes
    (welcome, MOTD, house and allegiance notices, the plugins' login probes); admin tells
    never count;
  - any line a plugin ate.
- **The engine reports eaten lines to RynthChat.** After every plugin has seen a line, the chat
  dispatch calls RynthChat's new `RynthChatNoteEaten` when a plugin ate it (one lookup per batch).
  With an older RynthChat, the export is missing and nothing happens.
- **Setting:** Chat filters > Standard filters > "Automated system text shows in System only".
  It is on by default, saved as `automatedToSystem` in `rynthchat_settings.json`; files without
  it load as on. Changing it re-routes the kept lines.
- The rule editor's test box names the step "Automated" when it applies.

## Tests

- `tools/ChatHistoryBench` (22/22): 8 new checks.
  - An automated line shows in System only, from any channel.
  - An unmarked line still shows under All.
  - With the setting off, automated lines show under All.
  - A rule that moves the line still decides first.
  - RynthChat's classifier and buffer: login-burst, admin-tell and eaten-line handling.
  - The bench also gets the standard-filter stub it lacked, so it builds again.
- The checks caught a crash before release. The new RynthChat regex read a timeout field that is
  declared further down the file, so it was still zero at startup and RynthChat failed to start.
  It is fixed in RynthChat 0.3.4.
- ChatFilterTests 1329/1329, ChatHoldTests 46/46, ChatTellTests 59/59; ChatDiscordTests and
  ChatCoordTests pass.

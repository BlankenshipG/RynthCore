# 2026.10.18.25: /rc chatlinkprobe (toward coordinate links in AC's own chat) (2026-10-18)

**Kind:** Diagnostic (Engine). Previous: 2026.10.18.24 (test installer), 2026.10.18.15 (last engine hot build).

## Why

Coordinates are links in RynthChat (2026.10.17.2), but not in AC's own chat window. ub-IT and VirindiTank
do it through Decal, which wraps coordinates in AC's clickable-name markup and catches the click with its
`ChatNameClicked` hook. RynthCore must do this without Decal, and no public source names the acclient
function that handles a click on a chat link. This probe finds that function by watching the client.

## What

`/rc chatlinkprobe [seconds|off]` (default 60 s, at most 600). While it runs:

- **Coordinates in AC's chat window become AC links.** Each pair is printed as
  `<Tell:IIDString:110011:42.1N, 33.6E>42.1N, 33.6E<\Tell>`, using the same link id as GoArrow and
  UtilityBelt. The rewrite happens only in what AC prints, through the engine's own chat hook (held
  lines, and lines printed at once on AC's main thread). Plugins and RynthChat still get each line as
  the server sent it. AC's own markup (players' names) is never rewritten.
- **Every message to `gmMainChatUI::ListenToElementMessage` is logged** (up to 400 per run): its
  first 12 dwords, and any text they point at.

Nothing changes while the probe is off.

## How to use it

1. `/rc chatlinkprobe 120`
2. Have coordinates appear in AC's chat, for example `/tell <yourself> 42.1N 33.6E`.
3. Click the coordinates in AC's chat window, then click a player's name.
4. Send the RynthCore log. The click's message tells the next build which call to hook so a click goes
   to RynthNav (arrow, or with Ctrl held a walk), with no Decal.

## Checks

Engine builds; ChatHoldTests, ChatCoordTests 49/49, ChatTellTests and ChatFilterTests pass.

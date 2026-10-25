# RynthChat: /myquests replies no longer wipe the chat history (2026.10.20.18)

## Symptom
Every so often the chat looked reset: older lines under All vanished at once
(noticed around banking, but unrelated to it).

## Cause
RynthAi's Meta Manager polls `/myquests` about every ten minutes. On a
well-played character the reply is about 2,200 lines (Silentkelpie's flight
dump of 21:51:29 shows bursts at 21:40:41 and 21:50:49). Those lines are
marked automated and shown only in the System tab, but they counted against
the same 2,000-line "Chat history" as everything else, so each reply pushed
every line under All out of the history.

## Fix
- `ChatHistory.Keep` / `Merge(..., extra)`: automated lines (ChatLine.Automated:
  /myquests and /qb replies, the login burst, lines a plugin ate) are kept on
  their own budget of the same length; ordinary lines keep the full setting.
  Order is kept. Worst case memory is twice the setting (about 3.6 MB at 2,000).
- `ChatSource`: the merge and the trim use it.
- `ChatFace.DropGone`: removes any shown line the source no longer keeps (from
  the middle as well as the top), keeping the view in place while scrolled up.

RynthChat's own ring (the plugin) still holds the newest lines of both kinds;
the engine reads it ten times a second, so it only matters on a bind or reload.

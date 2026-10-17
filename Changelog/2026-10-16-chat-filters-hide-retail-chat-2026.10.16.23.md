# 2026.10.16.23 — Chat filters hide lines in the retail chat too; settings follow other clients

**Kind:** Hot push (Engine). Previous `2026.10.16.22`.

## Problem

1. A standard filter (or a Hide rule) such as **Kill task progress** hid the line in RynthChat,
   but AC's own chat window still printed it ("You have killed 22556 Fanatical Fiuns! You must
   kill 100000 to complete your task.").
2. With several clients open, ticked standard filters came back off: every client writes its whole
   state to the one `%AppData%\RynthCore\rynthchat_settings.json` and never re-read it, so the
   last client to save overwrote the others' changes.

## Cause

1. RynthChat's rules and standard filters live in the engine and only decided where a line shows
   in RynthChat. AC's print path holds each incoming line for a verdict, but that verdict came only
   from plugins (their `eat` flag); the engine's own Hide decision was never asked.
2. No re-read of the shared settings file after startup.

## Fix

1. After the plugins' verdict on a held line, the engine runs the same rules and standard filters
   (`ChatModel.ShouldHideInRetail`: same channel the RynthChat plugin would give it, a separate
   router that doesn't double-count hits). A hidden line is kept out of AC's chat window.
   New option under **Standard filters**: *Hidden lines are hidden in the retail chat too*
   (on by default, saved as `hideInRetail`). Moved / copied lines still show in the retail chat.
2. Every client checks the settings file every 2 s and reloads rules, standard filters, tabs and
   display options when another client changed it (its own saves are recognised and skipped;
   its selected tab is kept; an unreadable file is ignored).

## Notes

- Only lines the engine holds can be kept out: with `/rc chathold off`, or if the plugin pump is
  stalled, a line prints before the verdict and shows as before.
- Re-tick **Kill task progress** / **Kill task complete** once: the shared file lost them.

## Checks

Engine builds; ChatFilterTests 1310, ChatHoldTests 46, ChatDiscordTests 316, ChatTellTests 59 pass.

# Engine 2026.10.18.29: incoming chat queue sized for 25,000-line quest replies (2026-10-19)

Previous release: 2026.10.18.25 (chat link probe).

## Why

On ACECustom, `/myquests` and `/qb list` print one chat line per quest flag. Infinite Leaftide
characters hold more than 15,000 flags, and the lists should cope with at least 25,000. The
plugins' incoming-chat queue (`PluginManager.MaxPendingIncomingChats`) held 4,096 lines and
dropped the oldest past that. A slow plugin pump during a 25,000-line burst would have lost the
head of the reply.

## Changed

- `MaxPendingIncomingChats` is 65,536. A queued line is a short string, so a full queue costs a
  few MB. Drops are still counted and logged at most every 10 s ("incoming chat line(s) dropped
  before plugins heard them").

Goes with RynthILT 0.2.22, RynthOracle 0.6.1 and RynthAi 0.5.91 (RynthSuite changelog
`2026-10-19-quest-lists-25000-flags-...`).

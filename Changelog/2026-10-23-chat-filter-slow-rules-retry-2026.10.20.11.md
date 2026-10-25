# Engine 2026.10.20.11: chat filter rules no longer switch off for good on long sessions

## Fixed

- Silentkelpie, about 48 hours into one session: the chat filter had "stopped working". Its rules
  were being switched off one by one: "ChatModel: rule "^\[RynthNet] Silentstorm connected\.$" is
  too slow (a match took over 15 ms) and is skipped until edited." That pattern takes
  microseconds. The 15 ms regex timeout is wall-clock time, so a pause of the whole thread during
  an easy match (a garbage collection, other processes scheduled in on a machine running several
  clients) threw it, and the rule stayed off until someone edited it. Over days, rule after rule
  went off. (The skip was never saved, so a restart brought them back, which is why it only
  showed on long sessions.)

## Changed

- A rule match (and the "colour the match" scan) that times out is run once more before the rule
  is judged slow: a pause doesn't repeat, a slow pattern does.
- A rule judged slow is skipped for 10 minutes and then tried again on its own; each further
  strike doubles the wait (20, 40 minutes ...), at most 4 hours. Editing the pattern still clears
  it at once. The log says when it is skipped (and for how long) and when it is tried again.
- The filter editor's status and test box say so ("skipped for a while ... tried again on its own").
- Unchanged: the per-pass budget for genuinely slow rules (100 ms per 500 kept lines), which
  already caps one reading at 30 ms so a single pause can't condemn a rule there.

## Tests

- tools/ChatFilterTests: "slow rules: a second chance, and tried again later" (the strike waits;
  an easy rule matches; a pattern that always times out fails twice; the router skips a slow rule
  and tries it again after its wait, then waits 20 minutes after a second strike; an edit clears
  it; an easy rule hides its line again once tried again). 1484/1484 checks passed.

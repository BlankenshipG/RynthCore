# 2026-11-15 — Item Tool: Quick give's Amount and Every fields

Silentkelpie: "on item tool add in a option to give x number of a item and a timer of how often every x
seconds."

- Item Tool > Quick give has two fields, side by side under the item and target picks:
  - **Amount (0 = all)**: how many a round gives.
  - **Every (s, 0 = once)**: the seconds between rounds; the give goes on until Stop.
- A value goes to RynthAi on Enter or a +/- click ("giveamount N", "giveevery N"). The field keeps the
  sent value until RynthAi's data shows it back.
- With a RynthAi older than this, the fields are hidden and the description is the old one.
- ItemToolData reads RynthAi's new keys: amount, everySec, amountMax, everyMax, rounds, units, nextInSec.

The giving itself is RynthAi's: RynthSuite/Changelog/2026-11-15-quick-give-amount-and-every.md.

# API request: merge-stack result (no slot assigned)

**For:** the RynthCore maintainer, who assigns plugin API numbers.
**Status:** request only. Nothing in `sk/features-on-rynth-main` uses it or takes a slot.

## Why

`MergeStackInternal(source, target)` returns true when it queues the merge or sends it. A plugin can't tell what happened next:

- whether the merge ran or was skipped because the target was full;
- whether the main-thread queue dropped it;
- whether AC refused it.

AutoStack today infers the outcome from a later inventory snapshot. That takes a couple of ticks, and it confuses "still in flight" with "refused".

## Proposed host function

```c
// Outcome of the most recent MergeStackInternal(source, target) for this exact pair.
// Returns a status code; *amount = units sent (Sent only), *ageMs = time since it was recorded.
// Either out-pointer may be null.
int GetMergeStackResult(uint sourceObjectId, uint targetObjectId, int* amount, int* ageMs);
```

| Code | Name | Meaning |
|------|------|---------|
| 0 | None | No request recorded for this pair, or it aged out |
| 1 | Queued | Accepted onto the main-thread queue, not run yet |
| 2 | Sent | `Event_StackableMerge` sent for `amount` units |
| 3 | TargetFull | Skipped: the target stack is already at its max stack size |
| 4 | Failed | Invalid ids, merge API unavailable, AC returned 0, or it threw |
| 5 | QueueFull | Main-thread queue full; dropped before it ran |

## Engine side (already written, waiting for a slot)

- `MergeStackResults` records the outcome at every exit of `ClientHelperHooks.MergeStackInternal`. It's a dictionary keyed by the (source, target) pair, capped at 512 entries, and entries older than 120 s are pruned.
- The SDK gets a `MergeStackStatus` constants class, plus `HasGetMergeStackResult`, gated on the assigned version and a non-zero function pointer.
- The SK branch had put this in v77, which collides with the UI hooks (and earlier with v68/v69). It has been removed. Once you pick a number, the function goes there, together with:
  - the `PluginContract` field;
  - the `RynthCoreApiNative` field;
  - the SDK wrapper.

## Plugin side (after a slot exists)

`InventoryManager` AutoStack would poll `GetMergeStackResult` after each merge:
- **Sent / TargetFull:** move on;
- **QueueFull / Failed:** retry later;
- **Queued:** wait.

On an engine without the function, it keeps today's snapshot check.

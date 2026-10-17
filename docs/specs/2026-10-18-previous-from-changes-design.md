# Design: Rebuild the Previous State from Changed Fields

**Issue:** [#90 Rebuild the previous state from changed fields](https://github.com/STotev/RuleLoom/issues/90)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 49–53, 56)
**Date:** 2026-10-18
**Status:** Approved (AFK)

---

## Problem

`changed` and `$previous.X` need the previous state, so a producer sends the whole subject twice: `current` and
`previous`. Usually only a few fields changed. A producer should be able to send `current` plus the **changed fields
with their old values**, and rules should behave as if the full previous state had been sent.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| API | `PreviousState.FromChanges(current, changes)` in `RuleLoom.Abstractions`, passed as the `previous` argument: `engine.ExecuteAsync(type, current, PreviousState.FromChanges(current, changes), rules)`. No new `ExecuteAsync` overload | An overload taking `(current, changes)` would clash with `(current, previous)` (both `object`). The value works anywhere a previous state goes (`EvaluationContext`, operators, actions). In 0.7, `RuleEvent` can carry `changes` and build it internally |
| How previous is built | **Not copied.** `PreviousState` is an overlay: a path is looked up in `changes` first and falls back to `current` | No cloning or serialization of `current`, so unchanged values keep their exact types (a .NET `Guid` stays a `Guid`, so `changed` doesn't see a type difference). Works the same for .NET objects and JSON state |
| Format of `changes` | A JSON object (`JsonElement` or `JsonObject`), nested like the state; anything else throws `ArgumentException` | The wire format is JSON. JSON tells an explicit `null` ("was null") from a missing key ("didn't change"); a .NET object can't. In-process callers have the full previous object anyway |
| Lookup rules | Keys match like paths (exact case wins, otherwise a unique case-insensitive match). For a path `A.B.C`: if `changes` has the whole path, its value is the previous value. If the walk reaches a value that isn't an object (e.g. `"A": null`) before the end, the previous value is `null` (the parent itself changed). If a key is missing, the previous value comes from `current` | A changed parent replaces everything below it; untouched branches come from `current` |
| Path ending at a changed object | Returns the partial object from `changes` (JSON) | `changed Customer` is true when anything under `Customer` changed, which is what listing it in `changes` means. Comparing a partial object with `eq` isn't meaningful and isn't supported |
| `$previous` alone | Returns the `PreviousState` (with `Current` and `Changes` properties) | There's no materialized previous object; actions that need old values resolve paths |
| Keys only in `changes` | Used as the previous value (current resolves to `null`), so `changed` reports the field as changed | E.g. a discount that was removed |

## Usage

```json
{
  "current": { "Status": "Paid", "Total": 640.5, "Customer": { "Email": "ada@example.com" } },
  "changes": { "Status": "AwaitingPayment" }
}
```

```csharp
var previous = PreviousState.FromChanges(request.Current, request.Changes);
var result = await engine.ExecuteAsync("OrderUpdated", request.Current, previous, rules);
// $previous.Status = "AwaitingPayment"; $previous.Total = 640.5 (from current); changed Total = false
```

## Test plan

- **`PreviousStateTests`** (through `EvaluationContext.Resolve`), for `JsonElement`, `JsonNode` and .NET `current`:
  - changed leaf; unchanged leaf from current; nested change; sibling of a nested change from current;
  - `null` in changes (previous is null); a parent replaced by `null` (deeper paths null);
  - key only in changes; case-insensitive keys; ambiguous keys fall back to current;
  - `$previous` alone returns the state;
  - non-object `changes` throws.
- **`ChangedOperatorTests`:** `changed` with a `PreviousState` (changed, unchanged, set to null, changed nested object).
- **Integration:** a JSON rule on JSON state with `changes` (`changed Status` and `$previous.Status eq "AwaitingPayment"`).

## Docs

- JSON guide: "Sending only the changed fields" under *Rules on JSON State*.
- `AGENTS.md`: `PreviousState` in Key concepts.

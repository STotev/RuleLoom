# Design: Case-Insensitive Names

**Issue:** [#37 API hardening: case-insensitive names](https://github.com/STotev/RuleLoom/issues/37)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleLoom/issues/22) (module 5 and the case policy)
**Date:** 2026-10-06
**Status:** Approved (AFK)

---

## Problem

JSON property names in rules are matched ignoring case, but every *name inside* a rule is matched exactly:

- `"operator": "EQ"` fails with `Operator 'EQ' not found`.
- `"type": "Send-Email"` doesn't find the `send-email` action.
- A rule with `"eventType": "orderCreated"` never runs for the `OrderCreated` event.
- `{"path": "price"}` silently resolves to `null` on a `Price` property.

Hand-written and UI-generated rules are easy to get wrong this way, and the failures are mostly silent.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| What ignores case | Operator names, action types, event types, property path segments | PRD #22 case policy |
| What doesn't | String **values** (`"Bob"` ≠ `"bob"`), action parameter names | Values are data; parameter names are handled with `GetParameter<T>` in #35 |
| Comparison | `StringComparison.OrdinalIgnoreCase` / `StringComparer.OrdinalIgnoreCase` | Culture-independent and fast; names are identifiers, not text |
| Duplicate registrations | Operators or action types that differ only in case are rejected at registration, with a message naming both, e.g. `Operator 'EQ' conflicts with 'eq' (names are case-insensitive).` | Otherwise one would silently shadow the other |
| Property resolution | Each path segment first tries an **exact-case** public instance property; only if there is none, a case-insensitive match. Indexers are ignored | Exact case wins (PRD user story 16), so existing paths resolve exactly as before |
| Ambiguous segment | If there's no exact match and several properties match ignoring case (e.g. `Price` and `PRICE` for `price`), the segment is treated as missing and resolves to `null`, like any unknown property | Picking one would be arbitrary. Rare in practice |
| Scope prefixes | `current.` / `previous.` already ignore case; unchanged (they become `$current.` / `$previous.` in #38) | |
| Event type `null` | Rules whose `EventType` is `null` never match (only possible for rules built in code that bypass `required`) | |
| Caching | The path cache stays keyed by the exact path string; `"price"` and `"Price"` get separate entries pointing to the same property | Simple; lookups after the first are still cached |

## Behaviour changes

Previously, these failed or silently didn't match; now they work:

| Rule | Before | After |
|---|---|---|
| `{"operator": "EQ", ...}` | `Operator 'EQ' not found` | evaluated as `eq` |
| action `"type": "Log"` | `Failed: not supported` | runs the `log` executor |
| `"eventType": "orderCreated"` on `OrderCreated` | rule not considered | rule considered |
| `{"path": "price"}` on `Price` | `null` | the price |

And one registration that used to work is now rejected: two operators (or two action executors) whose names differ
only in case.

## Test plan

- `ConditionEvaluatorTests`: operators found in any case; duplicates differing only in case rejected with the
  message above.
- `DirectActionFactoryTests`: action types found in any case; duplicates differing only in case rejected.
- `RuleEngineTests`: event types match in any case.
- `PathResolverTests` / `EvaluationContextTests`: segments resolve in any case, nested and scoped; exact case wins
  over a case-insensitive match; ambiguous segments resolve to `null`; indexers are ignored.
- Integration: a JSON rule written entirely in unusual case (operator, event type, path, action type) runs.

## Breaking changes (`feat!:`)

- Registering operators or action executors whose names differ only in case now throws `ArgumentException`.
- Rules that previously didn't match because of case now match, and may run actions they didn't run before.

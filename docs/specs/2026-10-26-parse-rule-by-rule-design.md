# Design: Parsing Rule Arrays Rule by Rule

**Issue:** [#81 Parse rule arrays rule by rule](https://github.com/STotev/RuleLoom/issues/81)
**Parent PRD:** [#113 Typed actions and authoring endpoints (0.7)](https://github.com/STotev/RuleLoom/issues/113) (user stories 75–76)
**Date:** 2026-10-26
**Status:** Implemented

---

## Problem

`RuleJsonParser.ParseMany` throws on the first malformed rule, so one bad rule in a file rejects every rule in it.
Problems found by `RuleValidator` only skip that rule; parse problems should too. `MapRuleLoom`'s `POST /validate`
(#118) needs the same per-rule outcome for an array.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| API | `RuleJsonParser.ParseEach(string json)` returns `IReadOnlyList<RuleParseResult>`, one per array element in order | A new name for a new contract; the caller decides what to do with malformed rules |
| `ParseMany` | Kept, all or nothing | Rules in code or tests want a failure, not results to inspect |
| Result | `RuleParseResult(int Index, RuleDefinition? Rule, RuleParseException? Error)` with `IsParsed` (`Rule` is set) | The index is always known; the error's `Path` locates the problem (`$[2].condition`). An id isn't read from a malformed rule: it may be the malformed part |
| Errors per rule | The first problem (the parser stops at it, as `Parse` does) | The shape check is throw-based; one actionable error per rule is enough to fix it |
| Malformed JSON, or not an array | Throws (`JsonException`, `RuleParseException` at `$`), as `ParseMany` | Nothing to split into rules |
| Deserialization errors | A value of the wrong type that the shape check doesn't cover (`"priority": "high"`, `"mode": "Weekly"`) becomes a `RuleParseException` at the rule's location (`$[1].priority`), in `Parse`, `ParseAsync` and `ParseMany` too | Per-rule, it's a malformed rule like any other; before, `ParseMany` reported it relative to the rule (`$.priority`) as a plain `JsonException` |
| Sample | `TenantRuleStore` uses `ParseEach`: a malformed rule is reported with its file and location, the rest of the file loads | Acceptance criterion |

## Deviations

None.

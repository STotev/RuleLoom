# Design: Ordering Operators `gt`, `gte`, `lt`, `lte`

**Issue:** [#36 API hardening: gt, gte, lt and lte operators](https://github.com/STotev/RuleForge/issues/36)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleForge/issues/22) (module 4)
**Date:** 2026-10-06
**Status:** Approved (AFK)

---

## Problem

Rules can only test equality and change. Thresholds such as "price above 1,000,000" or "due before a date" need
ordering operators, and the README even showed a hand-written `gt` as a custom-operator example.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Operators | `gt` (>), `gte` (≥), `lt` (<), `lte` (≤); exactly two operands each, left compared to right | Names follow `eq`: short and lowercase |
| Comparison rules | `OperatorContext.TryCompare`, i.e. `ValueComparer.TryCompare` (#30) | One set of rules for every operator: numbers across types, enums by name or number, dates as instants including ISO 8601 strings, ordinal strings, same-type `IComparable` |
| Incomparable values | Evaluate to `false`, never throw | PRD user story 13: one odd value shouldn't fail the rule. This includes `null` on either side, `NaN`, a number vs a non-numeric string, a date vs a non-ISO string, and different enum types |
| Consequence | `gt` and `lte` are **not** complements: for incomparable values both are `false` | Same as SQL's handling of `NULL`; `not` + `gt` is the way to say "not greater than, or unknown" |
| Implementation | Four small sealed classes, each one line over `TryCompare` | Simplest; no shared base class for four one-liners |
| Registration | Added wherever the built-in operators are listed (sample, guides, tests) | There is no central "default operators" list yet; DI (#5) will provide one through `AddRuleForge()` |

## Behaviour

| Rule (JSON values) | Domain value | `gt` | `gte` | `lt` | `lte` |
|---|---|---|---|---|---|
| `Price` vs `100` | `decimal 150` | true | true | false | false |
| `Price` vs `100` | `decimal 100` | false | true | false | true |
| `DueDate` vs `"2026-05-01"` | `2026-04-30T23:00Z` | false | false | true | true |
| `Status` vs `"Active"` | `Status.Inactive` (= 2 > 1) | true | true | false | false |
| `Name` vs `"b"` | `"a"` | false | false | true | true |
| `Price` vs `"abc"` | `decimal 100` | false | false | false | false |
| `Discount` vs `5` | `null` | false | false | false | false |

## Test plan

- `OrderingOperatorTests`: one matrix of (left, right, expected `gt`/`gte`/`lt`/`lte`) rows run against all four
  operators through a test-built `OperatorContext` — greater, equal and less cases across numeric types, dates
  (including ISO strings), enums and strings — plus incomparable pairs and nulls, which give `false` for all four.
  One matrix instead of four classes keeps the operators' relationships visible in each row.
- Engine-level JSON test: a rule using `gte` and `lt` on a `decimal` and a `DateTimeOffset` property matches.
- `ConditionEvaluatorTests` lists the new operators' names and operand limits.

## Out of scope

- `between`, `in`, string, collection and date operators (backlog).
- A central list of built-in operators (#5).

# Design: ISO 8601 String Dates for Schedule `ReferencePath`

**Issue:** [#33 API hardening: ISO 8601 string dates for schedule ReferencePath](https://github.com/STotev/RuleLoom/issues/33) (also closes #19)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleLoom/issues/22) (user story 44)
**Date:** 2026-10-06
**Status:** Approved (AFK)

---

## Problem

`ScheduleCalculator` parsed string reference values with its own lenient rules (`DateTimeOffset.TryParse` with
invariant culture, allowing whitespace and formats such as `04/02/2026` or `2026-04-02 10:00:00`), while rule
comparisons (#30) accept only strict ISO 8601. The same string could be a valid date in a schedule and never match in
a condition. #19 decided to keep string support but restrict it to ISO 8601.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Conversion | `ValueConverter.TryConvert<DateTimeOffset>` (#30) for every resolved reference value | One set of date rules: `DateTime`/`DateTimeOffset` as UTC instants (`Unspecified` = UTC), strings as strict ISO 8601 (no offset = UTC, date-only = UTC midnight) |
| Non-ISO strings | `InvalidOperationException` with an example of accepted formats → the action's result is `Failed` | Previously accepted formats such as `04/02/2026` are now rejected; this is the #19 decision |
| Other types | Unchanged: `InvalidOperationException` naming the type | |
| `null` | Unchanged: `InvalidOperationException` ("resolved to null") | |

## Breaking change

Reference values that were non-ISO strings (e.g. `"2026-04-02 10:00:00"`, `"04/02/2026"`, leading whitespace) used to
schedule; they now produce a `Failed` action result.

## Test plan

- ISO 8601 strings: `Z`, no offset, `+02:00`, minutes only, date-only → correct UTC instant.
- Non-ISO strings → `InvalidOperationException` mentioning ISO 8601.
- Unsupported types (e.g. `int`) and `null` → descriptive errors, as before.

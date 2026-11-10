# Design: Time Semantics (Time Zone, Dates and Date-Times)

**Roadmap:** [Time semantics review](../roadmap.md#time-semantics-review-needs-a-proper-breakdown), the first part of
0.8 (Scheduling)
**Decision:** [ADR 0004](../adr/0004-time-zone-and-calendar-dates.md); terms in `GLOSSARY.md` (Time zone, Date-time,
Date)
**Date:** 2026-10-28
**Status:** Agreed in a design review with the maintainer; planned in [PRD #134](https://github.com/STotev/RuleLoom/issues/134)

---

## Problem

Every date conversion goes through `ValueConverter.TryConvertToDateTimeOffset`, which assumes UTC when no zone is known:
`DateTime` with `Kind = Unspecified` (what EF Core and most drivers return) is relabelled UTC, ISO text without an
offset is UTC, and date-only text is UTC midnight. Outside UTC this shifts comparisons and schedules by hours, and a
date compared with a date-time can land on the wrong day. `DateOnly` isn't treated as a date at all. The assumption is
spread over `ValueConverter`, `ValueComparer` and `ScheduleCalculator`.

## Decisions

### The time zone

| Question | Decision |
|---|---|
| Meaning of a date-time without an offset | Wall-clock time in a configured time zone (IANA id), default UTC, so nothing changes until an app configures one. An offset always wins |
| Where it's configured | On the catalog: `CatalogBuilder.UseTimeZone("Europe/Sofia")`; written in the catalog document as `"timeZone"` for authoring UIs. An unknown id fails the catalog build at startup (so a container without time-zone data fails immediately) |
| Per evaluation | Each `EvaluationContext` is given a `TemporalRules` (the catalog's), so a later per-evaluation override (e.g. from the `RuleEvent` envelope) hands it another instance without changing the converter. 0.8 builds neither the override nor a way to configure one; decided with `RuleEvent` |
| `DateTimeKind.Local` | Kept: the machine's zone (`TimeZoneInfo.Local`). Documented as machine-dependent and discouraged when rules run on several machines |
| Times of day (`TimeOnly`, "after 18:00") | Out of scope; they come with path functions or a time-of-day operator (backlog) |

### `TemporalRules`

A public, sealed, immutable `TemporalRules` in `RuleLoom.Abstractions.Values`, created from a time zone (`TemporalRules.Utc`
the default). It holds the whole policy; nothing else decides time-zone questions.

- **Classify** a value: a date (`DateOnly`, ISO date-only text), a date-time (`DateTime`, `DateTimeOffset`, ISO text
  with a time), or neither.
- **Resolve** a date-time to a moment: an offset wins; `Local` in the machine's zone; no offset in the time zone.
- **Calendar date** of a date-time in the time zone.
- **Start of day** of a date in the time zone; **add calendar days** to a moment, keeping its wall-clock time.

`ValueConverter` and `ValueComparer` take a `TemporalRules` (UTC by default for existing callers); comparisons stay in
`ValueComparer`, which requires the rules internally and no longer decides time-zone questions. `EvaluationContext`
carries the catalog's instance; operators reach it through `ctx.AreEqual` / `ctx.TryCompare`. `ScheduleCalculator` and
`RuleValidator` use the same instance. It's built on `TimeZoneInfo`; a move to NodaTime later stays behind it.

### Dates

| Question | Decision |
|---|---|
| Date-only values | Calendar dates, never converted to moments implicitly: `DateOnly` and date-only text (in state, rule values and parameters) compare with each other by day |
| A date with a date-time | The date-time's calendar date in the time zone, then compared as dates (type-aware: the kinds of both values decide). `PlacedAt eq "2026-05-01"` means placed on 1 May in the time zone; `gt "2026-05-01"` means from 2 May |

### Schedules

| Question | Decision |
|---|---|
| Units | `Days` are calendar days in the time zone, keeping the wall-clock time across daylight-saving changes; `Seconds`, `Minutes` and `Hours` are elapsed time |
| A date as the reference | The start of that day in the time zone; documented explicitly |

### Times that don't exist or occur twice

| Source | Doesn't exist (spring forward, 02:30) | Occurs twice (fall back, 01:30) |
|---|---|---|
| Times RuleLoom calculates (schedules) | Lenient: moved forward by the gap | The earlier occurrence |
| Values the app supplies without an offset (state, rule values, parameters) | Invalid: rejected | The earlier occurrence |
| `DateTimeKind.Local` values | Invalid: rejected | Native .NET conversion: preserves the known occurrence where available; otherwise normally assumes standard time (the later occurrence during a typical fall-back) |

So: `Local` values use the machine's zone, impossible ones throw `InvalidTemporalValueException`, and ambiguous ones keep
native .NET semantics, because a `DateTime` from `ToLocalTime()` can carry which occurrence it is, while an ordinary
constructed `Local` value doesn't necessarily. `Unspecified` values use the catalog's time zone and choose the earlier
occurrence. Preserving an already-known moment wins over every ambiguous wall-clock value resolving the same way.

### Invalid data and unsupported conversions

The key distinction is between a **failed conversion** and a **validly expressed wall-clock date-time that can't exist
in its zone**.

| Situation | Behaviour |
|---|---|
| Unrecognised text in a generic comparison (no known temporal type) | Incomparable, with the validator's existing warning |
| Text that isn't ISO for a field known to be a date or date-time | The existing type-validation diagnostic, at its existing severity (a warning in 0.8); a severity policy, separate from temporal resolution |
| A valid offset-less date-time falling into a daylight-saving gap | `InvalidTemporalValueException` |
| A valid date-time in a repeated daylight-saving hour | Resolved as in the table above |
| A valid date-time with an explicit offset | That moment, as supplied |

- **Unsupported** values (`true`, non-ISO text): `TryConvert` returns `false`; comparisons treat them as incomparable,
  as today.
- **Invalid** values: `TemporalRules` throws a public, sealed `InvalidTemporalValueException`; `ValueConverter` and
  `ValueComparer` don't catch it, so the engine fails the rule (condition) or the action (schedule reference,
  parameter), with the message in `Error`.

**Runtime diagnostics** name the invalid value and the time zone, and the field or path when it's known: for a path,
the path as resolved (including the part continued on the runtime value, for JSON state or `object` properties).

**`RuleValidator`** checks date and date-time literals whose expected type is known, against the catalog's time zone:
condition values compared with a date or date-time field, and action parameter values whose type comes from the
action's parameters schema (not the event schema). An impossible wall-clock time is an error at the literal's JSONPath.
A schedule's `referencePath` is checked for its declared field type only; the timestamp it reads is checked at run
time.

**Two kinds of location**: a validation problem's JSONPath is where the offending literal or reference is written in
the rule document (`$.condition.operands[1]`); a runtime error names the event property path the value came from
(`PaymentDueAt`). The two aren't mixed.

### Breaking changes (0.8, `feat!`, no compatibility switch)

With the default zone (UTC), date-times keep their results. The upgrading guide documents:

- comparisons between dates and date-times (`"2026-05-01T10:00Z" gt "2026-05-01"` was true, is false);
- `DateOnly` values as dates;
- schedules from a date reference starting the day in the time zone, and `Days` as calendar days;
- action parameter conversion (`GetParameter<DateTime/DateTimeOffset/DateOnly>`) through the catalog's rules;
- `Local` values: impossible times are now rejected.

## Slices (issues when 0.8's PRD is written)

1. `TemporalRules`, the catalog's time zone (`UseTimeZone`, the document's `timeZone`), conversions and comparisons
   through it, `InvalidTemporalValueException`.
2. Dates as calendar days (`DateOnly`, date-only text) and comparisons between dates and date-times; the validator's
   schema-aware checks.
3. Schedules: calendar days, date references, lenient resolution of calculated times.

## Deviations

Recorded for slice 1 ([#135](https://github.com/STotev/RuleLoom/issues/135)):

- **No UTC defaults in the public API.** `ValueConverter.TryConvert` and `ValueComparer.AreEqual`/`TryCompare` take a
  `TemporalRules` parameter instead of defaulting to UTC, so no caller assumes UTC without saying so
  (`TemporalRules.Utc` is explicit). Operators keep `ctx.AreEqual`/`ctx.TryCompare`, which pass the evaluation's rules.
- **`GetParameter<T>(name)` reads UTC.** It has no evaluation context, so no catalog; `GetParameterAsync` and
  `GetParametersAsync` read with the context's rules. Typed parameters are bound with converters built on the rules,
  which also fixes System.Text.Json reading offset-less text as a `DateTimeOffset` in the machine's offset.
- **A stored document's `timeZone` is ignored**, like its operators and actions: the time zone is configuration in
  code (`UseTimeZone`); the document only reports it.
- **Date-only text until slice 2**: still a moment, the start of its day in the time zone (a skipped midnight moves
  forward), until [#136](https://github.com/STotev/RuleLoom/issues/136) makes dates calendar days.
- **Naming the source**: `InvalidTemporalValueException.Path` is set for values read from a rule path (operands, `changed`,
  schedule reference paths, `$path` parameters); a literal parameter is named in the message ("Parameter 'sendAt' of
  action 'remind' is …").

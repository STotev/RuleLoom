# Design: Time Semantics (Time Zone, Dates and Date-Times)

**Roadmap:** [Time semantics review](../roadmap.md#time-semantics-review-needs-a-proper-breakdown), the first part of
0.8 (Scheduling)
**Decision:** [ADR 0004](../adr/0004-time-zone-and-calendar-dates.md); terms in `GLOSSARY.md` (Time zone, Date-time,
Date)
**Date:** 2026-10-28
**Status:** Agreed in a design review with the maintainer; issues are cut when 0.8's PRD is written

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
| Per evaluation | Each `EvaluationContext` carries the catalog's rules; a later per-evaluation override (e.g. from the `RuleEvent` envelope) hands it another instance. Not built in 0.8 unless `RuleEvent` needs it |
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
| `DateTimeKind.Local` values | Invalid: rejected | Native .NET conversion (standard time, the later occurrence), as today |

### Invalid data and unsupported conversions

- **Unsupported**: the value isn't a date or date-time (`true`, non-ISO text). `TryConvert` returns `false`;
  comparisons treat the values as incomparable, as today.
- **Invalid**: a wall-clock time that doesn't exist in its zone. `TemporalRules` throws a public, sealed
  `InvalidTemporalValueException` naming the value and the zone; `ValueConverter` and `ValueComparer` don't catch it, so
  the engine fails the rule (condition) or the action (schedule reference, parameter), with the message in `Error`.
- **Schema-aware**: where a schema says a field is a date or a date-time, the problem names the field. `RuleValidator`
  checks rule values and parameter values compared with or bound to such fields against the catalog's `TemporalRules`:
  invalid wall-clock times are errors at the value's JSONPath, and text that isn't ISO for a date or date-time field
  stays a warning (as today for comparisons).

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

None yet.

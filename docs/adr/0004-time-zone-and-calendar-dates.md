# Date-times without an offset are read in the catalog's time zone; dates are calendar days

A catalog has a time zone (an IANA id, UTC unless configured). A date-time that states its offset is that moment; one that doesn't (`DateTimeKind.Unspecified`, ISO text without an offset) is wall-clock time in the catalog's time zone. A date (`DateOnly`, `"2026-05-01"`) is a calendar day, never a moment: a date and a date-time compare by the date-time's calendar day in the time zone, and a schedule from a date starts at the beginning of that day. One public service, `TemporalRules`, holds the whole policy, so the converter, the comparer and the scheduler never decide time-zone questions themselves. It's built on `TimeZoneInfo`, not NodaTime, behind that service.

## Considered Options

- **Keep reading offset-less date-times as UTC.** Rejected: most databases return them as wall-clock time in a business time zone, so rules and schedules drift by hours outside UTC.
- **Reject offset-less date-times.** Rejected: it breaks the most common input (EF Core and most drivers return `Unspecified`).
- **The server's local time.** Rejected: results would depend on where the app is deployed.
- **A time zone per field in the event schema.** Precise, but every schema author pays for it; the catalog's time zone is the default, and a per-evaluation override can come later through `TemporalRules`.
- **Dates as midnight instants (today's behaviour).** Rejected: "placed after 1 May" would match 1 May at 18:00, and a date compared with a date-time on the same day could differ.
- **NodaTime.** Its bundled time-zone database is consistent across machines, but it would be the first dependency of `RuleLoom.Abstractions` for features 0.8 doesn't need (periods, recurrence, business calendars). Revisit if those arrive; the public API doesn't change.

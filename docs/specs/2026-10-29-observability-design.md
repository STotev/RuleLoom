# Design: Observability (OpenTelemetry and Logging)

**Milestone:** 0.8 (Scheduling); replaces the planned "execution observer hook"
**Date:** 2026-10-29
**Status:** Agreed in a design review with the maintainer; issues are cut when 0.8's PRD is written

---

## Problem

The engine reports everything in its results and nothing else: no logs, no traces, no metrics. An app can record
results itself (`ExecuteAsync` returns a `RuleEngineResult`, `ExecuteScheduledAsync` an `ActionResult`), but it can't
see how long actions and context loads took, catalog problems found at startup reach no one unless the app looks, and
"why didn't my rule fire?" has no answer outside a debugger. Libraries an app uses alongside RuleLoom (Hangfire,
Quartz, MassTransit) log and trace; RuleLoom should fit in.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Scope in 0.8 | Operational telemetry (OpenTelemetry traces and metrics) and logging. **Execution history** (an observer hook to record results) is deferred to the operations dashboard and execution store (post-1.0) | Only RuleLoom can measure time per action and context load; history is already possible from the results, and a hook designed before its main consumer would likely be wrong |
| Telemetry API | `System.Diagnostics` `ActivitySource` and `Meter`, both named `RuleLoom`, in the `RuleLoom` package | Part of .NET: no new dependency; zero cost when nothing listens; apps that export OpenTelemetry get it by adding the source and meter |
| Logging API | `ILogger` (`Microsoft.Extensions.Logging.Abstractions`) in the `RuleLoom` package; the engine takes an optional `ILoggerFactory` (default `NullLoggerFactory`), `AddRuleLoom` passes the app's. Messages via `[LoggerMessage]` (no cost when a level is off). `RuleLoom.Abstractions` and `RuleLoom.Schemas` stay without it | Libraries of this kind log their own information; the abstraction is the standard and small |

### Traces

| Span | When | Attributes |
|---|---|---|
| `ruleloom.execute` | One per `ExecuteAsync` call | Event type, subject, event id; counts of rules matched, not matched and failed, of actions executed, scheduled, skipped and failed. A failed rule adds a span event (rule id, error) |
| `ruleloom.execute_scheduled` | One per `ExecuteScheduledAsync` call | Event type, subject, event id, rule id, action name and index, the status and skip reason |
| `ruleloom.action` | A child per action that runs | Action name and index, rule id, status; error status and message on failure |
| `ruleloom.context` | A child per context load (and subject load) | Context or provider name; error status on failure |

Rules aren't spans: an event with hundreds of rules, mostly not matching, would be noise; the counts and failure
events carry what matters.

### Metrics

| Instrument | Tags |
|---|---|
| Counter: rules evaluated, by outcome (matched, not matched, failed) | Event type |
| Counter: actions, by status (executed, scheduled, skipped, failed) | Event type, action name |
| Histogram: duration of an event's processing (`ExecuteAsync`) and of a scheduled run | Event type |
| Histogram: duration of a context load, with failures counted | Context or provider name |

### Logs

| Level | What |
|---|---|
| Information | The catalog was built: counts of events, contexts, operators, actions; the time zone |
| Warning | Catalog problems (stored entries with broken links); a context that failed to load; a failed rule or action; an overdue action skipped |
| Debug | Each rule's and action's outcome for an event (matched / not matched; executed / scheduled / skipped with the reason), with the subject and event id |

Nothing at Trace in 0.8. Results remain the source of truth; logs and traces make them visible without app code.

### Data in logs and telemetry

- **Written**: names and identifiers from the catalog and the rules (event type, rule id and name, action name and index,
  context name), statuses, durations and error messages.
- **Never written**: event state, context data and parameter values (they may hold personal data, e.g. an email in
  `to`).
- **`Subject` and `EventId`**: span attributes (what people search for: "what happened to order-1001?"); in logs at Debug
  only, since a subject can itself be personal data.
- **Error messages** are written as they are. Some of RuleLoom's own messages quote the offending value so it can be
  fixed (an impossible wall-clock time, a parameter that can't be bound), and exceptions from actions and providers are
  the app's own text; apps with strict rules filter the `RuleLoom` log category or redact through their logging
  pipeline. Documented in the guide.

### Docs

A short observability section in the guides: adding the `RuleLoom` source and meter to an OpenTelemetry setup, the
log categories and levels, and what's never written.

## Deviations

None yet.

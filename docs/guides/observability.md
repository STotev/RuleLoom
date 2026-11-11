# Observability

RuleLoom reports what it does in three ways: its **results** (always; the source of truth), **OpenTelemetry** traces and
metrics, and **logs**. Traces, metrics and logs carry names and ids from the catalog and the rules, statuses, durations
and error messages. They never carry event state, context data or parameter values, which may hold personal data.

## Traces and metrics

RuleLoom uses .NET's own `ActivitySource` and `Meter`, both named `RuleLoom` (`RuleLoomDiagnostics.Name`), so there's
nothing to install in RuleLoom itself. Add them to your OpenTelemetry setup:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(RuleLoomDiagnostics.Name) /* ... your exporter */)
    .WithMetrics(metrics => metrics.AddMeter(RuleLoomDiagnostics.Name) /* ... your exporter */);
```

When nothing listens, they cost close to nothing.

| Span | When | Attributes |
|---|---|---|
| `ruleloom.execute` | each `ExecuteAsync` | `ruleloom.event_type`, `ruleloom.subject`, `ruleloom.event_id`; counts `ruleloom.rules.matched`/`not_matched`/`failed` and `ruleloom.actions.executed`/`scheduled`/`skipped`/`failed`; a `ruleloom.rule_failed` event per failed rule (its id and error) |
| `ruleloom.execute_scheduled` | each `ExecuteScheduledAsync` | the event's attributes, `ruleloom.rule_id`, `ruleloom.action`, `ruleloom.action_index`, `ruleloom.status`, `ruleloom.reason` when skipped |
| `ruleloom.action` | each action that runs | `ruleloom.action`, `ruleloom.rule_id`, `ruleloom.status`; an error status and exception on failure |
| `ruleloom.context` | each context load, and a scheduled action's subject load | `ruleloom.context`, `ruleloom.provider` (`ruleloom.subject_load` for the subject) |

Rules aren't spans: an event with hundreds of rules, mostly not matching, would be noise. Their outcomes are the
counts on the event's span.

| Metric | Tags |
|---|---|
| `ruleloom.rules` (counter) | `ruleloom.event_type`, `ruleloom.outcome` (`matched`, `not_matched`, `failed`) |
| `ruleloom.actions` (counter) | `ruleloom.event_type`, `ruleloom.action`, `ruleloom.status` |
| `ruleloom.execute.duration`, `ruleloom.execute_scheduled.duration` (seconds) | `ruleloom.event_type` |
| `ruleloom.context.duration`, `ruleloom.subject.duration` (seconds) | `ruleloom.provider`, `ruleloom.outcome` (`success`, `failure`) |

## Logs

The engine logs through `ILogger` (category `RuleLoom.Engine.RuleEngine`). `AddRuleLoom` passes your app's logger
factory; without DI, pass one to the `RuleEngine` constructor (`loggerFactory:`).

| Level | What |
|---|---|
| Information | The catalog was built (category `RuleLoom.Catalog`, with `AddRuleLoom`): counts and the time zone |
| Warning | Catalog problems; a context that failed to load; a failed rule or action; an overdue action skipped |
| Debug | Each rule's and action's outcome for an event, with the subject and the event id: "why didn't my rule fire?" |

The subject and the event id appear in logs only at Debug, because a subject can itself be personal data (e.g. a
customer's email). Error messages are written as they are: some of RuleLoom's own messages quote the offending value so
it can be fixed (an impossible wall-clock time, a parameter that can't be bound), and exceptions from your actions and
providers are your own text. If your rules on personal data are strict, filter or redact the `RuleLoom` categories in
your logging pipeline.

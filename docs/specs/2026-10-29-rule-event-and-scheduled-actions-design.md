# Design: The Event Envelope, Subjects and Running Scheduled Actions

**Milestone:** 0.8 (Scheduling), with the [time semantics](2026-10-28-time-semantics-design.md) spec
**Decisions:** [ADR 0005](../adr/0005-scheduled-actions-are-rechecked-when-due.md); terms in `GLOSSARY.md` (Subject,
Overdue)
**Builds on:** the 0.5 spike's [findings](2026-10-14-aspnetcore-sample-design.md#findings) (envelope, entry point,
keys) and the 0.7 sample's [findings](2026-10-26-sample-typed-actions-design.md#findings)
**Date:** 2026-10-29
**Status:** Agreed in a design review with the maintainer; planned in [PRD #134](https://github.com/STotev/RuleLoom/issues/134)

---

## Problem

The engine returns scheduled actions with their due time, but running them later is the app's code: the sample's job
repeats the engine's lookup and failure handling, and builds the evaluation context itself. Nothing says whether a
pending action is still wanted when it's due (the order may have been paid), rules may have been edited or disabled
meanwhile, and a scheduled action can only be stored in memory. Events also have no envelope: no subject, id or
metadata.

The principle, from the maintainer's existing system: **anything that runs later must pass its checks then, against data
loaded then.**

## Decisions

### The envelope (`RuleEvent`)

As decided after the 0.5 spike, unchanged except where noted:

```csharp
public sealed record RuleEvent(string EventType, object Current, object? Previous = null)
{
    public string? EventId { get; init; }      // passed through; deduplication is the app's or the transport's
    public string? Subject { get; init; }      // for event types without a declared subject (see Subjects)
    public string? Source { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; }   // app-defined, e.g. a tenant id
}
```

| Question | Decision |
|---|---|
| Running | `ExecuteAsync(RuleEvent, rules)` beside the existing overloads (shorthands for an event without envelope data); `EvaluationContext.Event` for actions and operators |
| `$event.` paths | A new root in `RulePath`: `$event.EventId`, `Subject`, `Source`, `OccurredAt` (a date-time) are typed for the validator; `$event.Metadata.<key>` isn't checked and isn't warned about (nothing describes metadata keys yet). `$previous.$event…` is a parse error |
| JSON | `ToJson()` / `Parse(json)`, camelCase; the previous state as `previous` or as `changes` (the changed fields' old values, `PreviousState.FromChanges`), never both; an envelope built from changes is written with `changes`. After a round trip the state is `JsonElement` |
| Time zone override | Not in 0.8 (the time semantics spec lets `EvaluationContext` take another `TemporalRules` later) |
| Schedules from now | Unchanged: no reference path means the `TimeProvider`'s now, not `OccurredAt` (write `"referencePath": "$event.OccurredAt"` for that) |

### Subjects

An event's **subject** is the entity it's about. The event schema declares it and, optionally, how to load it as it is now:

```csharp
Subject(o => o.Number).LoadedBy("order");                   // one field
Subject(o => new { o.Country, o.Postcode }).LoadedBy("zone");  // composite, as context references allow
```

| Question | Decision |
|---|---|
| The loader | A context provider (`ContextProvider<Order>`, `AddContextProvider<OrderProvider>("order")`), one per kind of entity, shared by every event type about it; optional per event type. It gets the subject as the request's reference (`GetReference<T>()`, a dictionary of parts when composite). It isn't a `@context`: rule authors see no second set of fields |
| Identity | Read from the event's data when the schema declares it; `RuleEvent.Subject` is then ignored. Event types without a declared subject (e.g. JSON state without a schema) use `RuleEvent.Subject` |
| Catalog | The loader provider must be registered (as for contexts); the catalog document marks the event's subject, so a UI can say that scheduled actions check current data |
| Validation | Paths a scheduled action reads (the rule's condition, the action's condition and parameters) are checked against the loader's schema too: a field the loaded subject lacks is an error at that path ("'ChangedBy' isn't a field of the order loaded when the action runs"). Events may carry fields their subject doesn't |
| Not found | The loader returns `null` (the order was deleted): the action is `Skipped` |

### Running a scheduled action

`ExecuteScheduledAsync(ScheduledAction scheduled, RuleEvent storedEvent, IEnumerable<RuleDefinition> rules,
CancellationToken)` returns an `ActionResult`. `rules` are the app's current rules, as for `ExecuteAsync`.

1. **The rule** is found by `scheduled.RuleId` among `rules`; missing (deleted or disabled) → `Skipped` ("rule 'x' is
   no longer active").
2. **The action** is the current rule's action at `scheduled.ActionIndex`; if it names another action or isn't
   scheduled any more (the rule was edited) → `Skipped` ("the rule's actions changed"). Otherwise the **current**
   definition runs, so corrected parameters apply.
3. **Overdue** (see below): skipped when configured.
4. **The checks**: the rule's condition and the action's condition are evaluated again. With a subject loader,
   `$current` is the subject loaded now and `$previous` is the stored event's previous state; contexts load fresh.
   Without a loader, both conditions see the stored event. Not matched → `Skipped`.
5. **The action** runs with the same state (parameters' `$path`s read it too), with `ExecuteAsync`'s failure rules
   (factory or action exceptions, null results → `Failed`).

What conditions mean when a scheduled action is due (with a loader):

| Condition | Meaning |
|---|---|
| `Total gte 500`, `Status eq AwaitingPayment`, `@customer.Tier eq gold` | Checked against current data |
| `changed Status` | The status still differs from before the event; undone → skipped |
| `Price lt $previous.Price` | Still below the price before the event |

### Results and keys

| Question | Decision |
|---|---|
| Status | `ActionExecutionStatus.ExecutedImmediately` is renamed `Executed`, for actions run by the engine and by `ExecuteScheduledAsync`; `Skipped` covers failed checks and the reasons above, with a message |
| `ScheduledAction` | A reference, not a copy: `RuleId`, `ActionIndex`, `ActionName` (to detect edits), `ScheduledFor`, `Key`. `ToJson()` / `Parse(json)` like `RuleEvent`, so persistent job stores (Quartz with a database, Hangfire) store both as text |
| Key | `{ruleId}:{actionIndex}:{subject}`: the app's job id, so the same pending action is never scheduled twice (a redelivered event replaces the job), and the app can delete one it knows is unwanted. `null` without a subject. A composite subject is written `country=BG;postcode=1000` (parts in declared order, invariant culture, `=`, `;` and `:` escaped). Tenancy is the app's: it prefixes keys itself (the dispatching guide shows it) |

### Due times in the past

| Question | Decision |
|---|---|
| A calculated due time already passed | The action runs immediately, within `ExecuteAsync` (`Executed`) |
| `Advance` without a reference path | Always in the past, so it runs immediately: a validator **warning** ("Advance from now runs immediately; did you mean a referencePath?") |
| Overdue actions | `RuleEngineOptions.SkipOverdueActions` (default off) and `OverdueGracePeriod` (`TimeSpan`, default 0): with skipping on, an action whose due time passed by more than the grace period is `Skipped` ("overdue by 3 hours, grace period 30 minutes"), both when the event is processed and when `ExecuteScheduledAsync` runs late (a scheduler that was down, a polling job). The grace period exists because schedulers don't fire exactly on time |
| Options | `RuleEngineOptions`, an engine option (how rules are run, unlike the catalog's time zone, which is how they're read), set through the `RuleEngine` constructor or `AddRuleLoom(r => r.Configure(o => ...))` |

### Out of this spec

- The execution observer hook: its own short design session before 0.8's PRD.
- The "dispatching scheduled actions" guide: a Quartz/Hangfire job as one call, keys on job and trigger, tenant
  prefixes, storing `ToJson()` text, the subject loader.

### Breaking changes (0.8)

`ExecutedImmediately` → `Executed`; `ScheduledAction`'s shape (a reference instead of `RuleName`, `Action` and
`Result`); due times in the past run immediately instead of being returned as `Scheduled`.

## Deviations

Recorded for the envelope slice ([#138](https://github.com/STotev/RuleLoom/issues/138)):

- **`ContextRequest.Event`** was added, so context providers can read the envelope (e.g. a tenant id in its metadata),
  like actions and operators do through `EvaluationContext.Event`.
- **`$event.EventType`** is readable too (the spec listed the other fields); an event run through the state overloads
  has only its type.
- **`occurredAt` must state its offset** in `RuleEvent.Parse`: an event's time is a moment, so it never depends on a
  time zone.
- **`$current.$event…`** is rejected like `$previous.$event…`; `$event` alone resolves to the `RuleEvent`; without a
  `RuleEvent` (an `EvaluationContext` from state alone), `$event.` paths resolve to `null`.

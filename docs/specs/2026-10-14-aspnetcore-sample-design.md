# Design: Central Rules-Service Sample (Spike)

**Issue:** [#70 Central rules-service sample (spike)](https://github.com/STotev/RuleLoom/issues/70)
**Parent PRD:** [#66 JSON state and central-service spike (0.5)](https://github.com/STotev/RuleLoom/issues/66) (user stories 25–31)
**Date:** 2026-10-14
**Status:** Approved (HITL)

---

## Problem

RuleLoom is meant to run inside a central rules service that receives events from other services as JSON
(`docs/vision.md`). Since #68 and #69 rules run on JSON state, but nobody has built such a service with the library
yet. The open questions (the event envelope, how a consumer runs scheduled actions later, keys and cancellation) can't
be settled on paper. This sample uses the library *as intended* and records what hurts; the findings go to #71.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Project | `samples/RuleLoom.Samples.RulesService`: an ASP.NET Core minimal API in the solution, referencing `RuleLoom` and `RuleLoom.Extensions.DependencyInjection` | Mirrors a real central service; packages it needs (Quartz) stay out of the libraries |
| Wire format | `POST /events` with `{ eventType, tenantId, entityId, current, previous? }`; `current`/`previous` bind as `JsonElement` | Minimal; `entityId` is needed for job keys. It's the sample's own format, not a library contract: what's missing becomes a finding |
| Tenants | `acme-shop` (orders) and `wheels-market` (car ads), one folder of rule files each | Two tenants with different rules and event types; reuses the console sample's domains |
| Rule loading | Every request reads `Rules/{tenantId}/*.json`, parses with `RuleJsonParser.ParseMany`, validates with `RuleValidator`; invalid files and rules are reported in the response and skipped. Unknown tenant → 404 | Storage and tenancy stay with the consumer; shows the recommended validate-then-run flow |
| Actions | Custom `SendEmailAction` (`send-email`, logs the email) plus the built-in `log` | At least one custom action registered with `AddRuleLoom` |
| Response | A DTO of the engine result (rules, action results, `scheduledFor`, error messages), the invalid rules and the scheduled job keys | `RuleEngineResult` holds `Exception`s, which don't serialize usefully |
| Scheduling | Quartz.NET (`Quartz.Extensions.Hosting`, in-memory store). One job per `ScheduledAction`, triggered at `ScheduledFor`, keyed `{tenantId}/{entityId}/{ruleId}/{actionIndex}` and scheduled with `replace: true` | The same event sent twice replaces the pending job instead of failing on a duplicate key. Nothing is cancelled (out of scope) |
| At due time | The job opens a DI scope, resolves the executor through `IActionFactory` and runs it with the event's snapshot (`new EvaluationContext(current, previous)`); no condition re-check | The plainest thing the current API allows; re-checking and stale state are findings for 0.8 |
| Verification | A `.http` file with requests per tenant, run manually; no test project | PRD decision |

## Rules

**`acme-shop`**
- `OrderPlaced`: VIP order (`Total gte 500`) → `send-email` to the customer.
- `OrderUpdated`: `changed Status` → `send-email` with the new status.
- `OrderPlaced`: payment reminder, `send-email` scheduled `Advance` 1 minute before `PaymentDueAt` (`ReferencePath`).
- `OrderPlaced`: big basket (`ItemCount gte 5`) → `log`.
- An invalid rule (unknown action `send-sms`) next to the big-basket rule, to show per-rule reporting.

**`wheels-market`**
- `AdPublished`: `log` the new ad; reminder `send-email` to the seller scheduled with a 30-second `Delay`.
- `AdUpdated`: price drop (`Price lt $previous.Price`) → `send-email` to the seller, plus `log` when `Price lt 10000`
  (action condition).

## `.http` file

Written for Rider's HTTP client. Requests: VIP `OrderPlaced` with `PaymentDueAt` two minutes ahead (set by a
pre-request script; the reminder fires about a minute later),
`OrderUpdated` with a status change and `previous`, an unchanged update, `AdPublished`, an `AdUpdated` price drop, and
an unknown tenant (404).

## Docs

- Sample `README.md`: how to run it, the wire format, the tenants and rules, what to watch in the console.
- Links from the root README and `docs/README.md`; roadmap updated.

## Findings

Collected while building the sample, then decided with the maintainer in
[#71](https://github.com/STotev/RuleLoom/issues/71) (2026-10-15). Each finding ends with its decision or follow-up.

### What worked

- JSON state needed nothing extra: `JsonElement`s from model binding go straight to `ExecuteAsync`. `changed`,
  `$previous.Price`, action conditions and an ISO `referencePath` all work on it, and `640.5` vs `640.50` is no change.
- `RuleValidator` caught a registration mistake precisely (`$.actions[0].action: No action is registered under the
  name 'log'`), and per-rule skipping kept the tenant's other rules running.
- The engine's inputs (`eventType`, `current`, `previous`, rules) were enough for evaluation. Tenant and entity ID were
  needed around the engine (to pick the rules and to key scheduled jobs), and an action that depends on the tenant
  (e.g. a per-tenant mail server) would need them too, but `EvaluationContext` only carries the state.

### Event envelope

- Sending the same event twice runs its immediate actions twice (two VIP emails). Deduplicating needs an event ID,
  which the sample's minimal wire format didn't have.
- Tenant and entity ID had no place in the library, so the sample carried them beside the engine, including into the
  scheduled job's data.

**Decision:** RuleLoom owns an envelope **contract**, `RuleEvent`, not a transport (recorded in
[`vision.md`](../vision.md#decided)):

```csharp
public sealed record RuleEvent(string EventType, object Current, object? Previous = null)
{
    public string? EventId { get; init; }     // the producer's message ID; deduplication stays with the consumer or transport
    public string? Subject { get; init; }     // what the event is about, e.g. "order-1001" (as in CloudEvents)
    public string? Source { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } // consumer-defined, e.g. tenantId
}
```

- `ExecuteAsync(RuleEvent, rules)` is added; the existing `ExecuteAsync(eventType, current, previous, rules)` overloads
  stay as shorthands for an event without metadata.
- Actions and custom operators read it from `EvaluationContext.Event`; rules reach it with **`$event.` paths**
  (`$event.Metadata.region` in conditions, `$event.OccurredAt` as a `referencePath`).
- The library defines its JSON shape (for producers and the 0.7 registration client) but never sends it.
- Deduplication by `EventId` is the consumer's or the transport's (e.g. an inbox); RuleLoom passes the ID through.
- Built in **0.7**, as planned.

### Running scheduled actions later

- No entry point for running a scheduled action later: `RunScheduledActionJob` repeats what the engine does for an
  immediate action (look up the executor by name, run it, turn exceptions into a failed result). Quartz can only
  schedule job classes; Hangfire can schedule a method call, but serializes it and needs a concrete type, while rules
  name actions. Either way, a thin job is the consumer's; the duplication is what hurts.
- An action run later reports `ExecutedImmediately`, the status name the engine uses for actions run without a schedule.
- Conditions aren't re-checked at due time, and the action sees the state of the event that scheduled it.

**Decision (0.8):** an engine entry point, `ExecuteScheduledAsync(scheduledAction, ruleEvent, cancellationToken)`, that
looks up the action and applies the same failure rules as `ExecuteAsync`, with a status that doesn't claim
"immediately". A Quartz or Hangfire job then becomes one call. Re-checking conditions is decided with cancellation.

### Keys and cancellation

- A stable key needs the rule's identity, but `RuleDefinition.Id` defaulted to a new GUID on every parse, and rules
  are re-read for every event. **Fixed in [#77](https://github.com/STotev/RuleLoom/issues/77):** `id` is required;
  identity comes from where the rule is stored.
- The entity part of the key came from the sample's wire format, outside the library.
- Replacing a pending action in Quartz needs the key on the trigger as well as the job: with a random trigger key the
  old trigger stays attached and the action runs twice (found by sending the same event twice).
- Replacing works by key, but nothing tells the consumer that a pending action is no longer wanted (e.g. the order was
  paid before the reminder).
- The job data holds `ScheduledAction` and the state as objects, which only works with the in-memory store; a
  persistent store needs both serialized (and `ActionDefinition` includes its condition).

**Decisions (0.8):** `ScheduledAction.Key` = `{ruleId}:{actionIndex}:{subject}`, with the subject from
`RuleEvent.Subject`, so a later event about the same entity can upsert or cancel the job. Cancellation (reporting keys
that are no longer wanted) remains the main open question for the scheduling PRD. Serializable scheduled actions and a
"dispatching scheduled actions" guide (including the trigger-key pitfall) are 0.8 items.

### Schema needs (0.6)

- Action parameters are static values: `send-email`'s `to` is a path by the action's own convention, and the subject
  can't include values from the state (no templating). The parameter descriptions planned for 0.6 should say whether a
  parameter is a value or a path, so `RuleValidator` and authoring UIs can treat it accordingly.
- Events arrive unchecked: a missing or mistyped field just resolves to `null`. Validating incoming events against
  their schema is already in the vision (*Under consideration*).
- Schemas could describe `$event.Metadata` keys once `RuleEvent` exists.

### API friction

- `log` is built in but `AddRuleLoom` doesn't register it (by design, see the DI spec); with it missing, every rule
  using it was skipped while the service answered 200. **Fixed in [#78](https://github.com/STotev/RuleLoom/issues/78):**
  renamed to `ConsoleLogAction` (`console-log`) so its development-only purpose is clear; it stays opt-in so it doesn't
  appear in action lists offered to users. Validating rules when they're saved, not only per event, would surface such
  mistakes earlier; worth a line in the guides.
- `RuleJsonParser.ParseMany` needs a JSON array, and one shape error rejects the whole file; only `RuleValidator`
  errors are per rule. Backlog: [#81](https://github.com/STotev/RuleLoom/issues/81).
- Due times relative to now need a client-side script in the `.http` file (Rider pre-request script).
- Quartz 4 moved hosting into the `Quartz` package (`Quartz.Extensions.Hosting` is empty) and changed `IJob.Execute`
  to `ValueTask Execute(context, cancellationToken)`; worth knowing for a future scheduler integration package.

### Named instances

Not needed by the spike: the tenant travelled beside the engine ("group per tenant" in Quartz terms) without friction,
and will travel in `RuleEvent.Metadata`. They stay *under consideration* in the vision.

## Deviations

- **Actions:** `AddRuleLoom` doesn't register the built-in `log`, so the sample adds `AddAction<LogAction>()`.
- **Job keys:** the trigger gets the same key as the job, so `ScheduleJobOptions.Replacing` replaces the pending
  action instead of adding a second trigger.
- **Rules:** `acme-shop` also has a "Big basket" `log` rule, in the same file as the invalid rule, to show per-rule
  skipping. "Price drop" emails the seller and adds a `log` action with an action condition (`Price lt 10000`) instead
  of emailing watchers. The payment reminder's reference is `PaymentDueAt`.
- **URL:** `http://localhost:5080` is set in `appsettings.json` (`Urls`), not a launch profile: Rider started the
  project without `launchSettings.json`, on Kestrel's default port 5000.
- **Package:** Quartz **4.3.0** (the `Quartz` package; 4.4 and 4.5 were days old).

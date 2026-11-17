# Design: The ASP.NET Core Sample on 0.8 Scheduling, and the Dispatching Guide

**Issue:** [#143 Sample and guides: dispatching scheduled actions](https://github.com/STotev/RuleLoom/issues/143)
**Parent PRD:** [#134 Time, scheduled actions and observability (0.8)](https://github.com/STotev/RuleLoom/issues/134) (user stories 110–111)
**Builds on:** [envelope and scheduled actions](2026-10-29-rule-event-and-scheduled-actions-design.md),
[observability](2026-10-29-observability-design.md), [time semantics](2026-10-28-time-semantics-design.md)
**Date:** 2026-11-17
**Status:** Implemented

---

## Problem

0.8's point is that an action scheduled for later is checked again when it's due, against current data. The sample
schedules a payment reminder but never shows it **not** firing, and its Quartz job keeps objects in memory, which a
persistent job store can't. Apps also need a guide to wiring their scheduler.

## Decisions

Reviewed with the maintainer before building (HITL).

| Question | Decision |
|---|---|
| Subject loader | `OrderPlaced` and `OrderUpdated` declare `Subject(o => o.Number).LoadedBy("order")`; an `OrderProvider : ContextProvider<Order>` reads an in-memory `OrderStore`, keyed by tenant (from `request.Event.Metadata`) and order number |
| Current data | `POST /events` saves each order event's current state into the `OrderStore`: it stands in for the shop's database, which the producer updated before sending the event |
| The demo | Order A awaiting payment schedules a reminder about a minute ahead; `OrderUpdated` marks A `Paid`, so the reminder is skipped when due ("no longer matches"); order B stays unpaid and gets its reminder |
| Envelope | The wire format gains optional `eventId` and `occurredAt`; the sample builds a `RuleEvent` with them, `Subject` = the entity id and `Metadata` = `{ tenantId }` |
| Job data | Text only: `ScheduledAction.ToJson()` and `RuleEvent.ToJson()`, as a persistent job store needs; job and trigger both keyed `{tenant}/{Key}` |
| Overdue actions | `r.Configure(o => { o.SkipOverdueActions = true; o.OverdueGracePeriod = TimeSpan.FromMinutes(1); })` |
| Observability | Debug logs for the `RuleLoom` categories in development; OpenTelemetry tracing with the `RuleLoom` source and meter and the console exporter (`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.Console`, Apache-2.0, sample only) |
| Hangfire | A snippet in the guide; Quartz stays the runnable sample |
| Guide | `docs/guides/dispatching-scheduled-actions.md`: the job as one call, keys on job and trigger, tenant prefixes, stored JSON, subject loaders, overdue actions |

## Findings

Recorded while building.

1. **Spans under ASP.NET Core requests were dropped.** OpenTelemetry's default sampler follows the parent span, and a
   request's activity isn't recorded unless the app adds ASP.NET Core instrumentation, so `ruleloom.execute` never
   reached the exporter; scheduled runs (started by Quartz, without a parent) did. The sample sets an always-on sampler
   with a comment; the observability guide now explains it. Apps usually add the instrumentation anyway.
2. **A scheduled run's span named a different subject than its key.** `ruleloom.execute_scheduled` and its Debug log
   use `RuleEvent.Subject` (`order-1001`), while the key and `ExecuteAsync`'s logs use the identity the schema
   declares (`SO-1001`). A library fix, in its own PR (see the issue's comments).
3. **The order store is a stand-in.** A service that receives events about another service's entities has no
   database of them; the subject loader needs one (or an API to the producer). The sample keeps an in-memory store
   current from the events themselves and says so; a real app calls the producer or reads its own projection.
4. **Two subjects in one request.** The wire format's `entityId` becomes `RuleEvent.Subject`, but order events
   declare their own subject (the order number), which wins; ads use `entityId`. Both work, but a reader sees two ids
   for one order. Keeping one (declared subjects only) would be clearer in a later tidy-up.
5. **The samples could be organised better** (maintainer's note while reviewing the shape): more separation by
   concern (events, contexts, scheduling, endpoints) would make them easier to navigate and to test. Candidate for a
   follow-up issue.
6. **Testing the sample is manual** (`.http` and the console). A small test project driving `POST /events` with a fake
   clock would catch regressions such as finding 1; part of the follow-up in 5.

## Deviations

None.

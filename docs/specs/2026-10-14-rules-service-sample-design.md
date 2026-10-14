# Design: Central Rules-Service Sample (Spike)

**Issue:** [#70 Central rules-service sample (spike)](https://github.com/STotev/RuleForge/issues/70)
**Parent PRD:** [#66 JSON state and central-service spike (0.5)](https://github.com/STotev/RuleForge/issues/66) (user stories 25–31)
**Date:** 2026-10-14
**Status:** Approved (HITL)

---

## Problem

RuleForge is meant to run inside a central rules service that receives events from other services as JSON
(`docs/vision.md`). Since #68 and #69 rules run on JSON state, but nobody has built such a service with the library
yet. The open questions (the event envelope, how a consumer runs scheduled actions later, keys and cancellation) can't
be settled on paper. This sample uses the library *as intended* and records what hurts; the findings go to #71.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Project | `samples/RuleForge.Samples.RulesService`: an ASP.NET Core minimal API in the solution, referencing `RuleForge` and `RuleForge.Extensions.DependencyInjection` | Mirrors a real central service; packages it needs (Quartz) stay out of the libraries |
| Wire format | `POST /events` with `{ eventType, tenantId, entityId, current, previous? }`; `current`/`previous` bind as `JsonElement` | Minimal; `entityId` is needed for job keys. It's the sample's own format, not a library contract: what's missing becomes a finding |
| Tenants | `acme-shop` (orders) and `wheels-market` (car ads), one folder of rule files each | Two tenants with different rules and event types; reuses the console sample's domains |
| Rule loading | Every request reads `Rules/{tenantId}/*.json`, parses with `RuleJsonParser.ParseMany`, validates with `RuleValidator`; invalid files and rules are reported in the response and skipped. Unknown tenant → 404 | Storage and tenancy stay with the consumer; shows the recommended validate-then-run flow |
| Actions | Custom `SendEmailAction` (`send-email`, logs the email) plus the built-in `log` | At least one custom action registered with `AddRuleForge` |
| Response | A DTO of the engine result (rules, action results, `scheduledFor`, error messages), the invalid rules and the scheduled job keys | `RuleEngineResult` holds `Exception`s, which don't serialize usefully |
| Scheduling | Quartz.NET (`Quartz.Extensions.Hosting`, in-memory store). One job per `ScheduledAction`, triggered at `ScheduledFor`, keyed `{tenantId}/{entityId}/{ruleId}/{actionIndex}` and scheduled with `replace: true` | The same event sent twice replaces the pending job instead of failing on a duplicate key. Nothing is cancelled (out of scope) |
| At due time | The job opens a DI scope, resolves the executor through `IActionFactory` and runs it with the event's snapshot (`new EvaluationContext(current, previous)`); no condition re-check | The plainest thing the current API allows; re-checking and stale state are findings for 0.8 |
| Verification | A `.http` file with requests per tenant, run manually; no test project | PRD decision |

## Rules

**`acme-shop`**
- `OrderPlaced`: VIP order (`Total gte 500`) → `send-email` to the customer.
- `OrderUpdated`: `changed Status` → `send-email` with the new status.
- `OrderPlaced`: payment reminder, `send-email` scheduled `Advance` 1 minute before `DueAt` (`ReferencePath`).
- An invalid rule file (unknown action) to show the reporting.

**`wheels-market`**
- `AdPublished`: `log` the new ad; expiry reminder `send-email` scheduled with a short `Delay`.
- `AdUpdated`: price drop (`Price lt $previous.Price`) → `send-email` to watchers.

## `.http` file

Requests: VIP `OrderPlaced` with `DueAt` = `{{$datetime iso8601 2 m}}` (reminder fires about a minute later),
`OrderUpdated` with a status change and `previous`, an unchanged update, `AdPublished`, an `AdUpdated` price drop, and
an unknown tenant (404).

## Docs

- Sample `README.md`: how to run it, the wire format, the tenants and rules, what to watch in the console.
- Links from the root README and `docs/README.md`; roadmap updated.

## Findings (draft)

Collected while building; #71 turns them into the final findings and the envelope decision.

Expected (to confirm or reject while building):
- No library entry point for running a scheduled action later: the job repeats what the engine does (factory lookup,
  context, error handling).
- Action conditions aren't re-checked at due time, and the state is the snapshot from the scheduling event.
- Job keys need `ruleId` (optional in rules) and an entity ID that lives outside the library.
- Nothing is persisted: with a persistent job store, `ScheduledAction` and the state would have to be serialized.

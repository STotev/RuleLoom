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

## Findings (draft)

Collected while building; #71 turns them into the final findings and the envelope decision.

**What worked**
- JSON state needed nothing extra: `JsonElement`s from model binding go straight to `ExecuteAsync`. `changed`,
  `$previous.Price`, action conditions and an ISO `referencePath` all work on it, and `640.5` vs `640.50` is no change.
- `RuleValidator` caught a registration mistake precisely (`$.actions[0].action: No action is registered under the
  name 'log'`), and per-rule skipping kept the tenant's other rules running.
- The engine's inputs (`eventType`, `current`, `previous`, rules) were enough for evaluation. Tenant and entity ID were
  only needed around the engine: to pick the rules and to key scheduled jobs.

**Scheduling (input for 0.8)**
- No entry point for running a scheduled action later: `RunScheduledActionJob` repeats what the engine does for an
  immediate action (look up the executor by name, run it, turn exceptions into a failed result).
- An action run later reports `ExecutedImmediately`, the status name the engine uses for actions run without a schedule.
- Conditions aren't re-checked at due time, and the action sees the state of the event that scheduled it.
- Keys: a stable key needs `ruleId`, but `RuleDefinition.Id` defaults to a new GUID on every parse, and rules are
  re-read for every event, so a rule without an `id` gets a new key each time (the sample's rules all set one). The
  entity ID comes from the wire format, outside the library. Follow-up: [#77](https://github.com/STotev/RuleForge/issues/77)
  makes `id` required (identity comes from where the rule is stored).
- Replacing a pending action in Quartz needs the key on the trigger as well as the job: with a random trigger key the
  old trigger stays attached and the action runs twice (found by sending the same event twice).
- The job data holds `ScheduledAction` and the state as objects, which only works with the in-memory store; a
  persistent store needs both serialized (and `ActionDefinition` includes its condition).

**Envelope and idempotency (input for #71)**
- Sending the same event twice runs its immediate actions twice (two VIP emails). Deduplicating needs an event ID,
  which the minimal wire format doesn't have; whether that's the library's concern is part of the envelope decision.

**API friction**
- `log` is built in but `AddRuleForge` doesn't register it (by design, see the DI spec); with it missing, every rule
  using it was skipped while the service answered 200. Validating a tenant's rules when they change (or at startup)
  would surface that earlier; the sample only validates per event. Follow-up:
  [#78](https://github.com/STotev/RuleForge/issues/78) renames it to `ConsoleLogAction` (`console-log`) so its
  development-only purpose is clear; it stays opt-in so it doesn't appear in action lists offered to users.
- `RuleJsonParser.ParseMany` needs a JSON array, and one shape error rejects the whole file; only `RuleValidator`
  errors are per rule.
- Action parameters are static values: `send-email`'s `to` is a path by the action's own convention, and the subject
  can't include values from the state (no templating). Parameter kinds (value vs path) are a Schema System question.
- Due times relative to now need a client-side script in the `.http` file (Rider pre-request script).
- Quartz 4 moved hosting into the `Quartz` package (`Quartz.Extensions.Hosting` is empty) and changed `IJob.Execute`
  to `ValueTask Execute(context, cancellationToken)`; worth knowing for a future scheduler integration package.

## Deviations

- **Actions:** `AddRuleForge` doesn't register the built-in `log`, so the sample adds `AddAction<LogAction>()`.
- **Job keys:** the trigger gets the same key as the job, so `ScheduleJobOptions.Replacing` replaces the pending
  action instead of adding a second trigger.
- **Rules:** `acme-shop` also has a "Big basket" `log` rule, in the same file as the invalid rule, to show per-rule
  skipping. "Price drop" emails the seller and adds a `log` action with an action condition (`Price lt 10000`) instead
  of emailing watchers. The payment reminder's reference is `PaymentDueAt`.
- **URL:** `http://localhost:5080` is set in `appsettings.json` (`Urls`), not a launch profile: Rider started the
  project without `launchSettings.json`, on Kestrel's default port 5000.
- **Package:** Quartz **4.3.0** (the `Quartz` package; 4.4 and 4.5 were days old).

# RuleForge Vision

**Status:** Living document — started 2026-10-07; deployment and ownership questions discussed 2026-10-07. Add to
it as ideas come up; decisions move from *Under consideration* to *Decided* once discussed. The
[roadmap](roadmap.md) orders the work; this page explains what RuleForge is meant to be and who owns what.

---

## What RuleForge is

A **library**, consumed the way [Ocelot](https://github.com/ThreeMammals/Ocelot) is: RuleForge provides the engine
and building blocks, and consumers build and host their own service around it. Developers define *what's possible*
(operators, actions, events, schemas); end users define *business logic* as JSON rules, without code changes.

## Deployment models

Consumers use RuleForge in one of two ways (or both):

**1. A centralised rules service.** The consumer builds a dedicated service that receives events from other services
and runs the rules for them.

```
 Orders service ──┐                    ┌──────────────────────────────┐
 Billing service ─┼── events (JSON) ──▶│  Consumer's rules service    │──▶ actions (email, webhooks, …)
 CRM service ─────┘   over HTTP/queue  │  RuleForge + consumer's      │
                                        │  rule storage, hosting, UI   │
                                        └──────────────────────────────┘
```

**2. In-process in a microservice.** A service uses the engine internally, driven by its own domain events.

```
 ┌───────────────────────────────────────────────┐
 │  Orders service                                │
 │  domain event ──▶ RuleForge engine ──▶ actions │
 └───────────────────────────────────────────────┘
```

## Who owns what

| Layer | What belongs here |
|---|---|
| **Core library** | Engine, operators, conditions, value comparison, path resolution (on .NET objects *and* JSON), JSON rule format and parsing, validation, results, scheduling *calculation*, action naming, the schema model and builder, the transport-free catalogue, small store contracts (`ISchemaStore`), contracts consumers build on |
| **Optional packages** | DI (`RuleForge.Extensions.DependencyInjection`, done), ASP.NET Core endpoints for the catalogue (`MapRuleForge`), a producer-side schema registration client, a state snapshotter (EF Core integration later) and, later still, an operations dashboard with storage implementations |
| **Consumer-built** | The central service and its hosting, transport between services, **rule storage**, **schema storage** (`ISchemaStore` implementation), **multi-tenancy**, auth on the endpoints, dispatching scheduled actions (unless they use an integration package) |
| **Separate projects** | The **end-user rule-authoring UI**, built by the maintainer as its own project on top of the REST API above |

A useful reference for the optional layer is Hangfire: a core package, pluggable storage, and a separate dashboard.
For schemas the reference is Swagger / Swashbuckle: schemas generated at runtime from the types a service already has.

## Decided

| Topic | Decision | Why |
|---|---|---|
| **Rule storage** | The consumer's responsibility. The engine takes the rules to run as input (`ExecuteAsync(eventType, current, previous, rules)`) | Storage needs differ per system. Example: in a multi-tenant SaaS system, rules are stored per customer organisation and every event carries a tenant ID. Not every integration has tenants, so the library shouldn't assume a storage or tenancy model |
| **Multi-tenancy** | The consumer's responsibility (which rules apply to which tenant, and how tenant IDs travel with events) | Same reason |
| **Rule source abstraction** | None. Rules stay an `ExecuteAsync` parameter; an interface is added only if a library feature must load rules itself | Consumers load rules fresh per event anyway (so newly added rules apply on the next trigger). An `IRuleSource` would move the same query into a class and push tenancy into ambient DI, for no gain; procedural callers keep the plain call |
| **Scheduling dispatch** | The engine computes *when* (`ScheduledFor`) and returns `ScheduledActions` with their definitions; running them later is up to the consumer's scheduler (Hangfire, Quartz, …). Integration packages would help but aren't required | Consumers already run schedulers; RuleForge shouldn't force one |
| **Transport** | Not part of the **core** library: no message-bus code, and no HTTP endpoints in the core packages. Optional hosting packages (dashboard, schema API) may expose HTTP endpoints that consumers map into their own app | Consumers choose their own infrastructure; opt-in endpoints keep the core host-agnostic |
| **Rule-authoring UI** | A separate project (the maintainer's), not part of the library | Keeps the library UI-free; the UI consumes RuleForge through an API |
| **Packaging** | Unpublished; publishing is on hold pending a decision. While unpublished, breaking changes need no compatibility shims | See the roadmap's release notes |
| **JSON state** | Core path resolution understands `JsonElement`, `JsonNode` and `IDictionary<string, object?>` (so `ExpandoObject` too), with the same case rules as properties (exact match wins, ambiguous case-insensitive matches resolve to `null`). Values flow into `ValueComparer`/`ValueConverter` | A central service must evaluate rules on events it receives as JSON, without the producers' classes. `System.Text.Json` is part of the BCL, so `RuleForge.Abstractions` stays dependency-free. Schemas add typing and validation on top but aren't required |
| **Schemas: source of truth** | **Code-first**, like Swagger: the producer's .NET types (usually DTOs mapped from aggregates) describe the event; schemas are generated from them at **runtime** by reflection, with types inferred from the CLR | The authoring UI needs the fields a user can pick for a path operand, with labels; producers already have those types. Schema-first (generating classes in the rules service) would need a rebuild for every new event and is unnecessary once JSON resolves |
| **Schemas: configuration** | A **fluent builder** first (`schema.Event<OrderDto>("order.placed", e => e.Field(x => x.Total).Label("Order total"))`), with attributes later as a thin convention layer that calls the same builder. Precedence: CLR inference → attributes → explicit fluent calls | Builders are easier to test and experiment with and scale to more options than attributes; one model underneath (as in EF Core) |
| **Schemas: delivery and storage** | At runtime. Producers register their schemas with the central service (a registration client in an optional package); the service stores them through an `ISchemaStore` the consumer implements (tenancy inside it) | New events need no redeploy of the rules service; storage stays the consumer's, like rules |
| **Action parameters** | Actions describe their parameters with the **same builder** as event schemas | The UI renders action forms like event fields, and `RuleValidator` can check required and typed parameters; one description model |
| **Catalogue and REST API** | Core gets a transport-free **catalogue** (event schemas, operators with operand counts, actions with parameters, validate a rule) and DTOs. An optional ASP.NET Core package maps it: `app.MapRuleForge("/ruleforge")` → `GET schemas`, `GET operators`, `GET actions`, `POST rules/validate`, `POST schemas` (registration), returning the route group so the consumer adds auth and versioning. **No rule CRUD** | The authoring UI needs discovery and validation; rule storage is the consumer's, so the UI saves through the consumer's API, which calls RuleForge to validate |
| **State snapshotter** | Starts as a small **explicit API** that captures previous and current state (as JSON, optionally through the schema DTO mapping). EF Core change-tracking integration is a later optional package if there's demand | Explicit capture works anywhere; JSON snapshots need no classes on the receiving side |
| **Execution history** | Near term: a small core **observer hook** (or OpenTelemetry activities/metrics; to choose in its PRD) so consumers can record results wherever they like. The **operations dashboard** and an execution store with optional implementations come later, Hangfire-style | Consumers already get `RuleEngineResult`; a hook covers recording without committing the library to storage yet |

## Under consideration

Ideas the maintainer wants kept in the loop. None are scheduled; each needs discussion (and probably a PRD) first.

### Central rules-service sample (spike)
A minimal ASP.NET Core host that receives events as JSON (with a tenant ID), runs rules, and hands scheduled actions
to a scheduler — built by trying the library as intended. It settles the questions below that can't be decided on
paper: the event envelope, the scheduled-action entry point, and cancellation.

### Event envelope contract
A shared shape for events travelling to a central service: event type, current, previous, source service,
correlation ID, timestamp, schema version — and consumer-defined metadata such as a tenant ID. **To decide in the
spike**, by trying both:
- a `RuleEvent` record in Abstractions with an `ExecuteAsync(RuleEvent, rules)` overload, metadata exposed to actions
  (and perhaps to paths, e.g. `$event.TenantId`);
- or only an optional metadata parameter on `ExecuteAsync`, with the wire envelope left to the consumer.

Either way the library owns at most the *contract* and its serialization, not the transport.

### Running scheduled actions later
Decided so far:
- **State:** the consumer chooses — pass the event-time JSON snapshot or reload fresh state; the entry point just
  takes current/previous.
- **Cancel key:** `ScheduledAction` gets a deterministic key (rule ID + action index + an optional key path from the
  event, e.g. `InvoiceId`), so consumers can upsert or cancel jobs in Hangfire/Quartz.

Leaning, to confirm in the spike:
- **Entry point:** an engine method such as `ExecuteScheduledAsync(scheduledAction, current, previous?, rule?)` that
  resolves the action and applies the same failure rules as `ExecuteAsync`.
- **Re-checking conditions** at run time (only when the consumer passes the rule) — depends on cancellation below.

Open — **the main question for the scheduling PRD:** how later events cancel or replace pending actions. One model:
when the same event fires again for the same key, a still-matching rule returns the action again with the same key
(the consumer upserts the job), and a rule that no longer matches reports the key as **no longer wanted** (the
consumer cancels it). Today a `NotMatched` rule reports no scheduled actions at all. If this works, run-time
re-checks may be unnecessary ("scheduled means committed").

Also: past times, retries and duplicates; the **time semantics review** (time zones, `Unspecified` dates); and a
"dispatching scheduled actions" guide showing the Hangfire/Quartz pattern.

### Also on the roadmap backlog
Time semantics review, path variables and functions, more operators, benchmarks (#27), NuGet packaging — see the
[roadmap](roadmap.md#-backlog--not-yet-scheduled).

## More ideas

*(Add here as they come up.)*

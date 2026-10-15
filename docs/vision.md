# RuleLoom Vision

**Status:** Living document — started 2026-10-07; deployment and ownership questions discussed 2026-10-07; event
envelope decided after the central-service spike 2026-10-15. Add to
it as ideas come up; decisions move from *Under consideration* to *Decided* once discussed. The
[roadmap](roadmap.md) orders the work; this page explains what RuleLoom is meant to be and who owns what.

---

## What RuleLoom is

A **library**, consumed the way [Ocelot](https://github.com/ThreeMammals/Ocelot) is: RuleLoom provides the engine
and building blocks, and consumers build and host their own service around it. Developers define *what's possible*
(operators, actions, events, schemas); end users define *business logic* as JSON rules, without code changes.

## Deployment models

Consumers use RuleLoom in one of two ways (or both):

**1. A centralised rules service.** The consumer builds a dedicated service that receives events from other services
and runs the rules for them.

```
 Orders service ──┐                    ┌──────────────────────────────┐
 Billing service ─┼── events (JSON) ──▶│  Consumer's rules service    │──▶ actions (email, webhooks, …)
 CRM service ─────┘   over HTTP/queue  │  RuleLoom + consumer's      │
                                        │  rule storage, hosting, UI   │
                                        └──────────────────────────────┘
```

**2. In-process in a microservice.** A service uses the engine internally, driven by its own domain events.

```
 ┌───────────────────────────────────────────────┐
 │  Orders service                                │
 │  domain event ──▶ RuleLoom engine ──▶ actions │
 └───────────────────────────────────────────────┘
```

## Who owns what

| Layer | What belongs here |
|---|---|
| **Core library** | Engine, operators, conditions, value comparison, path resolution (on .NET objects *and* JSON), JSON rule format and parsing, validation, results, scheduling *calculation*, action naming, the schema model and builder, the transport-free catalogue, small store contracts (`ISchemaStore`), contracts consumers build on |
| **Optional packages** | DI (`RuleLoom.Extensions.DependencyInjection`, done), ASP.NET Core endpoints for the catalogue (`MapRuleLoom`), a producer-side schema registration client, a state snapshotter (EF Core integration later) and, later still, an operations dashboard with storage implementations |
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
| **Scheduling dispatch** | The engine computes *when* (`ScheduledFor`) and returns `ScheduledActions` with their definitions; running them later is up to the consumer's scheduler (Hangfire, Quartz, …). Integration packages would help but aren't required | Consumers already run schedulers; RuleLoom shouldn't force one |
| **Transport** | Not part of the **core** library: no message-bus code, and no HTTP endpoints in the core packages. Optional hosting packages (dashboard, schema API) may expose HTTP endpoints that consumers map into their own app | Consumers choose their own infrastructure; opt-in endpoints keep the core host-agnostic |
| **Rule-authoring UI** | A separate project (the maintainer's), not part of the library | Keeps the library UI-free; the UI consumes RuleLoom through an API |
| **Packaging** | Published to NuGet from 0.5.0 (`RuleLoom`, `RuleLoom.Abstractions`, `RuleLoom.Extensions.DependencyInjection`) by the release workflow, using nuget.org trusted publishing. Pre-1.0, minor versions can break; breaking changes are listed in the upgrading guide, without compatibility shims | See the roadmap's release notes |
| **JSON state** | Core path resolution understands `JsonElement` and `JsonNode` (what System.Text.Json produces without a class; JSON has no separate dictionary type), with the same case rules as properties (exact match wins, ambiguous case-insensitive matches resolve to `null`). Values flow into `ValueComparer`/`ValueConverter` | A central service must evaluate rules on events it receives as JSON, without the producers' classes. `System.Text.Json` is part of the BCL, so `RuleLoom.Abstractions` stays dependency-free. Schemas add typing and validation on top but aren't required. In-memory .NET dictionaries and legacy dynamic types (`ExpandoObject`) aren't supported |
| **Schemas: source of truth** | **Code-first**, like Swagger: the producer's .NET types (usually DTOs mapped from aggregates) describe the event; schemas are generated from them at **runtime** by reflection, with types inferred from the CLR | The authoring UI needs the fields a user can pick for a path operand, with labels; producers already have those types. Schema-first (generating classes in the rules service) would need a rebuild for every new event and is unnecessary once JSON resolves |
| **Schemas: configuration** | A **fluent builder** first (`schema.Event<OrderDto>("order.placed", e => e.Field(x => x.Total).Label("Order total"))`), with attributes later as a thin convention layer that calls the same builder. Precedence: CLR inference → attributes → explicit fluent calls | Builders are easier to test and experiment with and scale to more options than attributes; one model underneath (as in EF Core) |
| **Schemas: delivery and storage** | At runtime. Producers register their schemas with the central service (a registration client in an optional package); the service stores them through an `ISchemaStore` the consumer implements (tenancy inside it) | New events need no redeploy of the rules service; storage stays the consumer's, like rules |
| **Action parameters** | Actions describe their parameters with the **same builder** as event schemas | The UI renders action forms like event fields, and `RuleValidator` can check required and typed parameters; one description model |
| **Catalogue and REST API** | Core gets a transport-free **catalogue** (event schemas, operators with operand counts, actions with parameters, validate a rule) and DTOs. An optional ASP.NET Core package maps it: `app.MapRuleLoom("/ruleloom")` → `GET schemas`, `GET operators`, `GET actions`, `POST rules/validate`, `POST schemas` (registration), returning the route group so the consumer adds auth and versioning. **No rule CRUD** | The authoring UI needs discovery and validation; rule storage is the consumer's, so the UI saves through the consumer's API, which calls RuleLoom to validate |
| **State snapshotter** | Starts as a small **explicit API** that captures previous and current state (as JSON, optionally through the schema DTO mapping). EF Core change-tracking integration is a later optional package if there's demand | Explicit capture works anywhere; JSON snapshots need no classes on the receiving side |
| **Execution history** | Near term: a small core **observer hook** (or OpenTelemetry activities/metrics; to choose in its PRD) so consumers can record results wherever they like. The **operations dashboard** and an execution store with optional implementations come later, Hangfire-style | Consumers already get `RuleEngineResult`; a hook covers recording without committing the library to storage yet |
| **Event envelope** | A `RuleEvent` contract in Abstractions: `EventType`, `Current`, `Previous`, plus optional `EventId`, `Subject`, `Source`, `OccurredAt` and consumer-defined `Metadata` (e.g. a tenant ID). `ExecuteAsync(RuleEvent, rules)` is added beside the existing overloads; actions read it from `EvaluationContext.Event`, and rules reach it with `$event.` paths (`$event.Metadata.region`, `$event.OccurredAt`). The library defines the contract and its JSON shape, never the transport; deduplication by `EventId` stays with the consumer or transport. Planned for 0.7 | Decided after the central-service spike (#70, findings in its [spec](specs/2026-10-14-rules-service-sample-design.md#findings)): tenant and entity ID had no place in the library, actions couldn't see them, and duplicate events re-ran actions without an ID to deduplicate on. `Subject` also gives scheduled-action keys their entity part |

## Under consideration

Ideas the maintainer wants kept in the loop. None are scheduled; each needs discussion (and probably a PRD) first.

### Running scheduled actions later
Decided so far (the last two confirmed by the central-service spike, 2026-10-15):
- **State:** the consumer chooses — pass the event-time JSON snapshot or reload fresh state; the entry point just
  takes the event.
- **Entry point:** `ExecuteScheduledAsync(scheduledAction, ruleEvent)` resolves the action and applies the same failure
  rules as `ExecuteAsync`, with a status that doesn't claim "immediately". The spike's Quartz job had to repeat the
  engine's lookup and error handling.
- **Key:** `ScheduledAction.Key` = `{ruleId}:{actionIndex}:{subject}`, with the subject from `RuleEvent.Subject`
  (rule ids are required since #77), so consumers can upsert or cancel jobs in Hangfire/Quartz. Schedulers may need the
  key on more than the job (Quartz triggers), which the dispatching guide should show.

Open — **the main question for the scheduling PRD:** how later events cancel or replace pending actions. One model:
when the same event fires again for the same key, a still-matching rule returns the action again with the same key
(the consumer upserts the job), and a rule that no longer matches reports the key as **no longer wanted** (the
consumer cancels it). Today a `NotMatched` rule reports no scheduled actions at all. If this works, run-time
re-checks of conditions may be unnecessary ("scheduled means committed").

Also: past times, retries and duplicates; serializable scheduled actions for persistent job stores; the **time
semantics review** (time zones, `Unspecified` dates); and a "dispatching scheduled actions" guide showing the
Hangfire/Quartz pattern.

### Named instances (e.g. per tenant)
Raised 2026-10-08, after Quartz.NET's
[scheduler per tenant](https://github.com/quartznet/quartznet/blob/main/docs/documentation/quartz-4.x/multi-tenancy.md#scheduler-per-tenant):
`AddQuartz(name, …)` builds a fully separate scheduler per name, resolved as keyed services. Not sure yet whether
it's useful; kept here to evaluate.

The RuleLoom equivalent would be **named configurations**, with tenancy still the consumer's (the library doesn't
know what a tenant is; a name could equally be a region or a product line):

```csharp
foreach (var tenant in tenants)
    services.AddRuleLoom(tenant, rules => rules.AddAction<SendEmailAction>());

var engine = provider.GetRequiredKeyedService<RuleEngine>(tenantId);
```

- **What would differ per instance:** the registered operators and actions (so the catalogue, authoring UI and
  `RuleValidator` only offer what that tenant has), action implementations or settings (e.g. each tenant's SMTP),
  the `ISchemaStore`, the `TimeProvider` or a default time zone (ties into the time semantics review), and later the
  execution history.
- **Today's alternative** (Quartz's "group per tenant"): one shared engine, with the tenant ID in the event's
  metadata (`RuleEvent.Metadata`). The central-service spike worked this way without friction, so it showed no need
  for named instances yet.
- **Costs:** `AddRuleLoom` keeps one shared registry per service collection today, so it would need keyed
  registrations; the catalogue endpoints would need to know the instance (e.g. `MapRuleLoom("/{tenant}/ruleloom")`);
  adding instances at runtime (Quartz's `ISchedulerRuntime`) is a further step.
- **When to decide:** before the catalogue and endpoints (0.6–0.7) settle their shape.

### Typed rule consumers and dispatch (in-process, post-1.0)
Raised 2026-10-09, inspired by MassTransit consumers and FluentValidation. For a domain service using the engine
in-process: rules for an event type defined in code, with a fluent builder, and invoked mediator-style.

```csharp
[EventType("order.placed")]
public sealed class OrderPlacedRules : RuleConsumer<OrderPlaced>
{
    public OrderPlacedRules()
    {
        Rule("VIP welcome")
            .When(o => o.Customer.Tier).Equal("VIP")
            .And(o => o.Total).GreaterThan(100)
            .Then<SendEmailAction>(p => p.Template("vip-welcome"));
    }
}

await rules.PublishAsync(current: order, previous: before, ct);   // runs the consumers for OrderPlaced
```

- **The builder produces the existing model** (`RuleDefinition`: `o => o.Total` becomes the path `"Total"`,
  `.GreaterThan(100)` a `gt` condition). One engine and one execution path: code and JSON rules behave identically,
  code rules stay validatable, inspectable and exportable to JSON, and renames break the build. Compiling lambdas
  into delegates is explicitly not the design; it would be a second rule engine.
- **Typed inline actions** (`.Then(SomeMethod)`) for logic that doesn't need a reusable `IAction`. A free-form
  `Execute` method is questionable: logic that always runs is a handler, not a rule.
- **Dispatch** needs a CLR type ↔ event name registry, which the Schema System (0.6) builds anyway, so schemas,
  typed consumers and dispatch can share it. Dispatch could also add the caller's JSON rules for the same event.
- **Centralised engines:** transport adapters (e.g. a MassTransit integration) could carry the same events between
  services when the engine runs in a central service.
- **Builds on the fluent rule builder** (0.6), which already produces `RuleDefinition` in code; consumers add
  grouping by event type, typed inline actions and dispatch.

### Validating incoming events against schemas
Like MassTransit skipping messages of unknown types: once schemas are registered (0.6), a central service could reject
or skip events whose type has no schema, or whose payload doesn't fit it, instead of running rules that see `null`.
Optional strictness on top of untyped JSON support. To consider in the 0.6 design; the central-service spike confirmed
the gap (a missing or mistyped field just resolves to `null`).

### Also on the roadmap backlog
Time semantics review, path variables and functions, more operators, benchmarks (#27) — see the
[roadmap](roadmap.md#-backlog--not-yet-scheduled).

## More ideas

*(Add here as they come up.)*

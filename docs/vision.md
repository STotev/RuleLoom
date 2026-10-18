# RuleLoom Vision

**Status:** Living document — started 2026-10-07; deployment and ownership questions discussed 2026-10-07; event
envelope decided after the ASP.NET Core spike 2026-10-15. Add to
it as ideas come up; decisions move from *Under consideration* to *Decided* once discussed. The
[roadmap](roadmap.md) orders the work; this page explains what RuleLoom is meant to be and who owns what.

---

## What RuleLoom is

A **library**, consumed the way [Ocelot](https://github.com/ThreeMammals/Ocelot) is: RuleLoom provides the engine
and building blocks, and consumers build and host their own service around it. Developers define *what's possible*
(operators, actions, events, schemas); end users define *business logic* as JSON rules, without code changes.

## Deployment models

Consumers use RuleLoom in one of two ways (or both):

**1. Shared by several services.** The consumer builds a service that receives events from other services and runs
the rules for them.

```
 Orders service ──┐                    ┌──────────────────────────────┐
 Billing service ─┼── events (JSON) ──▶│  Consumer's service          │──▶ actions (email, webhooks, …)
 CRM service ─────┘   over HTTP/queue  │  RuleLoom + consumer's      │
                                        │  rule storage, hosting, UI   │
                                        └──────────────────────────────┘
```

**2. Inside a single service.** A service (or a monolith) uses the engine internally, driven by its own domain events.

```
 ┌───────────────────────────────────────────────┐
 │  Orders service                                │
 │  domain event ──▶ RuleLoom engine ──▶ actions │
 └───────────────────────────────────────────────┘
```

## Who owns what

| Layer | What belongs here |
|---|---|
| **Core library** | Engine, operators, conditions, value comparison, path resolution (on .NET objects *and* JSON), JSON rule format and parsing, validation, results, scheduling *calculation*, action naming, the schema model and builder, the transport-free catalogue, the schema store contract (`ISchemaStore`) with an in-memory store, contracts consumers build on |
| **Optional packages** | DI (`RuleLoom.Extensions.DependencyInjection`, done), ASP.NET Core endpoints for the catalogue and schema registration, schema publishing (HTTP first) and schema storage packages (e.g. PostgreSQL), a state snapshotter (EF Core integration later) and, later still, an operations dashboard with storage implementations |
| **Consumer-built** | The service that runs the rules and its hosting, transport between services, **rule storage**, **multi-tenancy**, a custom schema store if the shipped ones don't fit, auth on the endpoints, dispatching scheduled actions (unless they use an integration package) |
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
| **JSON state** | Core path resolution understands `JsonElement` and `JsonNode` (what System.Text.Json produces without a class; JSON has no separate dictionary type), with the same case rules as properties (exact match wins, ambiguous case-insensitive matches resolve to `null`). Values flow into `ValueComparer`/`ValueConverter` | A service must be able to evaluate rules on events it receives as JSON, without the producers' classes. `System.Text.Json` is part of the BCL, so `RuleLoom.Abstractions` stays dependency-free. Schemas add typing and validation on top but aren't required. In-memory .NET dictionaries and legacy dynamic types (`ExpandoObject`) aren't supported |
| **Schemas: source of truth** | **Code-first**, like Swagger: the producer's .NET types (usually DTOs mapped from aggregates) describe the event; schemas are generated from them at **runtime** by reflection, with types inferred from the CLR | The authoring UI needs the fields a user can pick for a path operand, with labels; producers already have those types. Schema-first (generating classes in the service that runs the rules) would need a rebuild for every new event and is unnecessary once JSON resolves |
| **Schemas: configuration** | One class per event, FluentValidation style (`class OrderPlacedSchema : EventSchema<OrderDto>` with `Event("OrderPlaced")`, `Field(o => o.Total).Label(...)`, `Context<CustomerDto>("customer")`), built on a fluent builder; the event type is always explicit. Types, nesting, nullability and labels are inferred from the CLR type; attributes may come later as a thin layer on the same builder. Precedence: inference → attributes → explicit calls. Lives in a dependency-free **`RuleLoom.Schemas`** package, so producers need neither the engine nor DI (decided for 0.6, PRD #87) | Classes are testable and keep each event in one place; explicit names mean less magic. Schemas are optional, so they stay out of `RuleLoom.Abstractions`, which action and operator authors reference (the Microsoft.Extensions / MassTransit split) |
| **Schemas: delivery and storage** | At runtime, as **JSON Schema (2020-12) with `x-ruleloom-*` extensions**; the document is the single stored representation (labels, ignored fields, contexts and references are all in it). Three separate concerns in `AddSchemas`: **sources** (where schemas come from: schema classes, exported JSON), **storage** (where they're kept where the engine runs: `ISchemaStore`, **provided by RuleLoom** like Hangfire's or MassTransit's storage: the in-memory store now, optional packages such as PostgreSQL later, a custom store still possible) and **publishing** (how a service that only raises events sends its schemas to the service that runs the rules: HTTP first, through registration endpoints mapped with ASP.NET Core). Sources are saved into the configured store at startup (0.6). Publishing, registration endpoints and storage packages come in 0.7; optional build-time generation too | New events need no redeploy of the service that runs the rules. Schemas are RuleLoom's own data (its format, keyed by event type), so the library ships their storage instead of every consumer writing one; rules stay the consumer's. JSON Schema lets UIs reuse existing form libraries |
| **Action parameters** | Described by **action descriptors**: a FluentValidation-style class per action that describes the action and its parameters (including whether a parameter is a value or a path), so external action authors ship the description with the action (0.7) | The UI renders action forms like event fields, and `RuleValidator` can check parameters. Actions are registered with the engine and may come from other authors, so they're described as a whole rather than through the event-schema builder |
| **Contexts** | An event has a **subject** (what changed: current and previous state, sent by the producer) and named **contexts**: related entities such as an order's customer or an ad's watchers, current state only, single or a collection, wherever they live (another service, the same service's repository). Rules use `@name.Path`. The engine loads a context through a **context provider** registered by name (`AddContextProvider<T>("customer")`), prefetched once per event when rules use it; actions load any context on demand. The **event schema declares the relation**: `Context("customer").ReferencedBy(o => o.CustomerId)` (and, when one provider serves several contexts, `Context("toAccount").LoadedBy("account")`), or a composite reference `ReferencedBy(o => new { o.Address.Country, o.Address.Postcode })` (EF Core style), optional for contexts that need none. The engine passes the resolved reference to the provider, so one provider serves several contexts and events. A **context's fields are described with its provider** (from `ContextProvider<T>`'s type), once, where the data comes from. The model is the same in a monolith, a single service or a service shared by others (PRD #87) | Rules need data from several entities, but shipping whole aggregates with every event is expensive. Previous state matters only for the subject, so related data can be loaded fresh and only when used. The producer knows how its event relates to other entities; the provider knows their shape |
| **Path sigils** | `$` for RuleLoom's variables (`$current`, `$previous`, later `$event`), `@` for consumer-defined contexts, `#` reserved for functions | Consumer names never collide with built-ins |
| **Smaller previous state** | Producers may send `current` plus only the **changed fields** with their old values; the engine rebuilds `$previous`. A full previous state still works (PRD #87) | Halves typical event payloads without losing `changed` and `$previous` |
| **Catalogue and REST API** | Core gets a transport-free **catalogue** (event schemas, operators with operand counts, actions with parameters, validate a rule) and DTOs. An optional ASP.NET Core package maps it: `app.MapRuleLoom("/ruleloom")` → `GET schemas`, `GET operators`, `GET actions`, `POST rules/validate`, `POST schemas` (registration), returning the route group so the consumer adds auth and versioning. **No rule CRUD** | The authoring UI needs discovery and validation; rule storage is the consumer's, so the UI saves through the consumer's API, which calls RuleLoom to validate |
| **State snapshotter** | Starts as a small **explicit API** that captures previous and current state (as JSON, optionally through the schema DTO mapping). EF Core change-tracking integration is a later optional package if there's demand | Explicit capture works anywhere; JSON snapshots need no classes on the receiving side |
| **Execution history** | Near term: a small core **observer hook** (or OpenTelemetry activities/metrics; to choose in its PRD) so consumers can record results wherever they like. The **operations dashboard** and an execution store with optional implementations come later, Hangfire-style | Consumers already get `RuleEngineResult`; a hook covers recording without committing the library to storage yet |
| **Event envelope** | A `RuleEvent` contract in Abstractions: `EventType`, `Current`, `Previous`, plus optional `EventId`, `Subject`, `Source`, `OccurredAt` and consumer-defined `Metadata` (e.g. a tenant ID). `ExecuteAsync(RuleEvent, rules)` is added beside the existing overloads; actions read it from `EvaluationContext.Event`, and rules reach it with `$event.` paths (`$event.Metadata.region`, `$event.OccurredAt`). The library defines the contract and its JSON shape, never the transport; deduplication by `EventId` stays with the consumer or transport. Planned for 0.7 | Decided after the ASP.NET Core spike (#70, findings in its [spec](specs/2026-10-14-aspnetcore-sample-design.md#findings)): tenant and entity ID had no place in the library, actions couldn't see them, and duplicate events re-ran actions without an ID to deduplicate on. `Subject` also gives scheduled-action keys their entity part |

## Under consideration

Ideas the maintainer wants kept in the loop. None are scheduled; each needs discussion (and probably a PRD) first.

### Running scheduled actions later
Decided so far (the last two confirmed by the ASP.NET Core spike, 2026-10-15):
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
  metadata (`RuleEvent.Metadata`). The ASP.NET Core spike worked this way without friction, so it showed no need
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
- **Engines shared by several services:** transport adapters (e.g. a MassTransit integration) could carry the same
  events between services.
- **Builds on the fluent rule builder** (0.6), which already produces `RuleDefinition` in code; consumers add
  grouping by event type, typed inline actions and dispatch.

### Scopes (applications, bounded contexts) and rule groups
Raised 2026-10-18. One engine could serve several applications in an organisation (a public website, a back-office
system, a set of microservices): each registers its own events, and users author rules only for the application they
work in, with forms built from that application's schemas. Three needs are distinct:

- **Tenancy**: *whose* rules (customer organisations). Stays the consumer's.
- **Scope**: *which application or bounded context* raised the event, and which events, operators, actions and contexts
  belong to it. Not modelled today; consumers would prefix event types (`orders.OrderPlaced`). Scope also separates two
  bounded contexts that use the same event name, so users can tell them apart.
- **Rule groups**: organising rules into sets that are enabled, disabled or ordered together.

Options for scope:
1. **Part of an event's identity**: the schema names its scope (e.g. `Event("OrderPlaced").In("orders")`), events are
   keyed by *(scope, event type)*, rules carry the scope, the catalogue filters by it, and the engine matches only rules of
   the event's scope. Fits `RuleEvent.Source` (0.7) and removes the prefixing boilerplate.
2. **Named instances** per application (above), when applications need different actions, operators or providers.
3. Both: scopes for events and rules, named instances only where capabilities differ.

A standard *(tenant, scope, event type)* key might also make an optional rule-store contract worthwhile, to cut the
boilerplate of loading the right rules and schemas. **To decide in the 0.7 PRD**, with the catalogue and `RuleEvent`.

### Per-event overrides of context fields
Raised 2026-10-18 during #92. A context's fields are described once, with its provider; an event may want to hide some
of them or label them differently for its rule authors (e.g. ignore a customer's internal fields for one event, or call
"tier" "Buyer tier" on a marketplace event). Not in 0.6; to consider with the 0.7 catalogue.

### Validating incoming events against schemas
Like MassTransit skipping messages of unknown types: once schemas are registered (0.6), a service could reject
or skip events whose type has no schema, or whose payload doesn't fit it, instead of running rules that see `null`.
Optional strictness on top of untyped JSON support. To consider in the 0.6 design; the ASP.NET Core spike confirmed
the gap (a missing or mistyped field just resolves to `null`).

### Also on the roadmap backlog
Time semantics review, path variables and functions, more operators, benchmarks (#27) — see the
[roadmap](roadmap.md#-backlog--not-yet-scheduled).

## More ideas

*(Add here as they come up.)*

# RuleForge Vision

**Status:** Living document — started 2026-10-07. Add to it as ideas come up; decisions move from *Under
consideration* to *Decided* once discussed. The [roadmap](roadmap.md) orders the work; this page explains what
RuleForge is meant to be and who owns what.

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
| **Core library** | Engine, operators, conditions, value comparison, JSON rule format and parsing, validation, results, scheduling *calculation*, action naming, contracts consumers build on |
| **Optional packages** | DI (`RuleForge.Extensions.DependencyInjection`, done) and, under consideration: scheduler integrations, an operations dashboard, a state snapshotter, a schema generator, a **schema / rule-authoring REST API** for authoring UIs |
| **Consumer-built** | The central service and its hosting, transport between services, **rule storage**, **multi-tenancy**, dispatching scheduled actions (unless they use an integration package) |
| **Separate projects** | The **end-user rule-authoring UI**, built by the maintainer as its own project on top of the REST API above |

A useful reference for the optional layer is Hangfire: a core package, pluggable storage, and a separate dashboard.

## Decided

| Topic | Decision | Why |
|---|---|---|
| **Rule storage** | The consumer's responsibility. The engine takes the rules to run as input (`ExecuteAsync(eventType, current, previous, rules)`) | Storage needs differ per system. Example: the maintainer's own system has firms (tenants); rules are stored per tenant and every event carries a tenant ID. Not every integration has tenants, so the library shouldn't assume a storage or tenancy model |
| **Multi-tenancy** | The consumer's responsibility (which rules apply to which tenant, and how tenant IDs travel with events) | Same reason |
| **Scheduling dispatch** | The engine computes *when* (`ScheduledFor`) and returns `ScheduledActions` with their definitions; running them later is up to the consumer's scheduler (Hangfire, Quartz, …). Integration packages would help but aren't required | Consumers already run schedulers; RuleForge shouldn't force one |
| **Transport** | Not part of the **core** library: no message-bus code, and no HTTP endpoints in the core packages. Optional hosting packages (dashboard, schema API) may expose HTTP endpoints that consumers map into their own app | Consumers choose their own infrastructure; opt-in endpoints keep the core host-agnostic |
| **Rule-authoring UI** | A separate project (the maintainer's), not part of the library | Keeps the library UI-free; the UI consumes RuleForge through an API |
| **Packaging** | Unpublished until decided otherwise; while unpublished, breaking changes need no compatibility shims | See the roadmap's release notes |

## Under consideration

Ideas the maintainer wants kept in the loop. None are scheduled; each needs discussion (and probably a PRD) first.

### Operations dashboard
A Hangfire-style UI for operators of a rules service — separate from the end-user authoring UI: recent executions,
matched/failed rules, failing actions, scheduled actions.
- Needs execution history, so it needs storage. Following Hangfire, the library could define a storage *interface*
  with optional implementations, while the consumer chooses where it lives.
- Probably an optional package hosted inside the consumer's service.

### State snapshotter (microservices)
A utility that makes it easy for a service to capture the **previous** and **current** state of an object when it
changes, and send both to the engine or to a central rules service.
- Where it hooks in: explicit API, EF Core change tracking, or domain-event helpers.
- What it produces: probably an event envelope (below), serialized as JSON.

### Event envelope contract
A shared shape for events travelling to a central service: event type, current, previous, source service,
correlation ID, timestamp, schema version — and consumer-defined metadata such as a tenant ID. The March roadmap's
`RuleEvent` was a first sketch. The library would own the *contract* and its serialization, not the transport.

### Schema System and generator
Event schemas describe the fields each event carries (types, labels). They drive rule-authoring UIs, path validation
and whitelisting (needed before end users author rules), typed JSON values, and per-field time zones.
- A **generator** builds schemas from a service's .NET event types, and can **auto-register** them with the central
  rules service at startup.
- Needs a schema format (JSON-Schema-like?) and a registration contract; the receiving endpoint is consumer-built.

### Schema / rule-authoring REST API
The authoring UI (a separate project) needs to discover what it can offer users: event schemas (fields, types, labels),
the registered operators (with operand counts) and actions (with their parameters), and a way to validate a rule
before saving it. RuleForge should expose this in a clean RESTful API. **To discuss** how best to do it:
- an optional ASP.NET Core package with endpoints the consumer maps into their own service
  (e.g. `app.MapRuleForge()` → `GET /schemas`, `GET /operators`, `GET /actions`, `POST /rules/validate`), the way
  Hangfire exposes its dashboard;
- or only the contracts/DTOs and a catalogue service, with the consumer writing the endpoints;
- whether rule CRUD belongs in it at all, given rule storage is the consumer's (likely: no — the UI saves through the
  consumer's own API, which can call RuleForge for validation);
- auth, versioning, and how schemas registered by other services (the generator above) appear in it.

### Evaluating rules on JSON state
Paths resolve on .NET objects, including anonymous objects and JSON deserialized into a known class. They do
**not** resolve on `JsonElement`, `Dictionary<string, object>`, `JsonNode` or `ExpandoObject`: every path silently
returns `null` (verified 2026-10-07). A central service that has the producers' classes (e.g. via a shared contracts
package) works today; one that receives arbitrary JSON doesn't.
- Options: document "deserialize into your contracts"; generate classes from schemas; teach path resolution about
  JSON and dictionaries; or make the Schema System the bridge.

### Running scheduled actions later
Even with dispatch left to consumers, they need a supported way to run a `ScheduledAction` when its time comes:
- an engine entry point (e.g. `ExecuteScheduledAsync`) instead of calling the action factory by hand;
- **which state** the action sees at run time: a snapshot taken at event time, or fresh state the consumer reloads;
- whether rule/action **conditions are re-checked** at run time (a cancelled viewing shouldn't trigger its reminder),
  which needs the rule again — supplied by the consumer, since storage is theirs;
- a stable key so later events can cancel or reschedule pending actions;
- past times, retries and duplicates; and the **time semantics review** (time zones, `Unspecified` dates).
- A "dispatching scheduled actions" guide showing the manual Hangfire/Quartz pattern.

### Rule source abstraction
With storage owned by the consumer, the open question is whether the library offers *anything* here — e.g. a small
read-only "rules for this event" interface that the scheduler entry point and a dashboard could call, with tenancy
handled in the consumer's implementation — or nothing at all.

### Also on the roadmap backlog
Time semantics review, path variables and functions, more operators, benchmarks (#27), NuGet packaging — see the
[roadmap](roadmap.md#-backlog--not-yet-scheduled).

## More ideas

*(Add here as they come up.)*

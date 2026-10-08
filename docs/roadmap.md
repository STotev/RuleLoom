# RuleForge Roadmap

**Last reviewed**: 2026-10-07

The single overview of what has been built, what is next, and what is planned but not yet scheduled.
Details live in GitHub issues and in [`specs/`](specs/); this page links to them rather than repeating them.

**Vision**: a library-first rule engine where developers define *what's possible* (operators, actions,
events, schemas) and end users define *business logic* (JSON rules) — without code changes. How it's meant to be
deployed and who owns what (core library, optional packages, consumer) is in [`vision.md`](vision.md).

## How work is planned

**PRD → phase issues → spec → implementation** (see [`AGENTS.md`](../AGENTS.md#planning-flow)).
New work is referred to by **issue number**. The old "Phase N.M" numbers below are kept only to
connect to historical docs — they were reused inconsistently between documents.

## Releases

| Version | Status | Contents |
|---|---|---|
| 0.1.0 | Tagged `v0.1.0` (release-please bootstrap; no GitHub release or NuGet package) | Core engine, operators, JSON parser, Phase 1.1 stability fixes |
| 0.2.0 | [Released](https://github.com/STotev/RuleForge/releases/tag/v0.2.0) 2026-10-02 | Action results, scheduling model, action factory, schedule calculation |
| 0.2.1 | [Released](https://github.com/STotev/RuleForge/releases/tag/v0.2.1) 2026-10-03 | Nested path fix (#14), shared path cache (#15) |
| 0.3.0 | [Released](https://github.com/STotev/RuleForge/releases/tag/v0.3.0) 2026-10-06 | PRD #22 API hardening (one breaking release; migration guide follows in #6) |
| 0.4.0 | [Released](https://github.com/STotev/RuleForge/releases/tag/v0.4.0) 2026-10-06 | Action names by registration, `IAction`, dependency injection, upgrading guide |

---

## ✅ Done

### Core foundation
Abstractions (`ConditionNode`, `IConditionOperator`, `IAction`), `RuleEngine`, `ConditionEvaluator`,
`PathResolver` with compiled-expression caching, operators `eq` `changed` `and` `or` `not`, JSON parser,
`LogAction`.

### Stability fixes (old Phase 1.1)
Priority ordering (higher first), operand-count validation, null-safe path resolution, continue-on-error
per rule, integration test project. Report: [`archive/phase-1-1-summary.md`](archive/phase-1-1-summary.md).

### Action scheduling & DI — PRD [#1](https://github.com/STotev/RuleForge/issues/1) ✅

| Issue | Slice | PR | Spec |
|---|---|---|---|
| [#2](https://github.com/STotev/RuleForge/issues/2) | Scheduling model & `ActionResult` | [#7](https://github.com/STotev/RuleForge/pull/7) | [scheduling-model-abstractions](specs/2026-03-28-scheduling-model-abstractions-design.md) |
| [#3](https://github.com/STotev/RuleForge/issues/3) | Action factory & per-rule results | [#9](https://github.com/STotev/RuleForge/pull/9) | [action-factory-pattern](specs/2026-03-28-action-factory-pattern-design.md) |
| [#4](https://github.com/STotev/RuleForge/issues/4) | Schedule calculation engine | [#10](https://github.com/STotev/RuleForge/pull/10) | [schedule-calculation-engine](specs/2026-03-29-schedule-calculation-engine-design.md) |
| [#55](https://github.com/STotev/RuleForge/issues/55) | Action names by registration, `"action"` in rule JSON | [#56](https://github.com/STotev/RuleForge/pull/56) | [action-names](specs/2026-10-06-action-names-design.md) |
| [#5](https://github.com/STotev/RuleForge/issues/5) | Dependency injection (`AddRuleForge`) | [#54](https://github.com/STotev/RuleForge/pull/54) | [dependency-injection](specs/2026-10-06-dependency-injection-design.md) |
| [#58](https://github.com/STotev/RuleForge/issues/58) | Rename `IActionExecutor` → `IAction` | [#59](https://github.com/STotev/RuleForge/pull/59) | — |
| [#6](https://github.com/STotev/RuleForge/issues/6) | Integration tests, samples, [upgrading guide](guides/upgrading.md), quick start rewrite | [#60](https://github.com/STotev/RuleForge/pull/60) | [integration-samples-upgrading](specs/2026-10-07-integration-samples-upgrading-design.md) |

Actually *dispatching* scheduled actions was out of scope for this PRD; see the vision's "Running scheduled actions
later".

### Code review bug batch (2026-10-02)
| Issue | Change | Fixed by |
|---|---|---|
| [#14](https://github.com/STotev/RuleForge/issues/14) | Unscoped nested paths (`Address.City`) threw | [#24](https://github.com/STotev/RuleForge/pull/24) |
| [#15](https://github.com/STotev/RuleForge/issues/15) | Path accessor cache recreated per event (~290× faster now) | [#26](https://github.com/STotev/RuleForge/pull/26) |
| [#20](https://github.com/STotev/RuleForge/issues/20) | Unit tests for operators, evaluator, path resolution (`RuleForge` coverage 57% → 90%) | [#29](https://github.com/STotev/RuleForge/pull/29) |
| [#21](https://github.com/STotev/RuleForge/issues/21) | Test packages aligned; xunit v3 on Microsoft.Testing.Platform; coverage in both projects | [#28](https://github.com/STotev/RuleForge/pull/28) |
| [#16](https://github.com/STotev/RuleForge/issues/16) · [#17](https://github.com/STotev/RuleForge/issues/17) · [#18](https://github.com/STotev/RuleForge/issues/18) · [#19](https://github.com/STotev/RuleForge/issues/19) | `changed` with scoped paths · clearer errors · cancellation checks · ISO-only string dates | folded into #38 · #34 · #32 · #33 |

### API hardening — PRD [#22](https://github.com/STotev/RuleForge/issues/22) ✅ (ships in 0.3.0)
Shared value comparison, `OperatorContext`, ordering operators, complete results, case-insensitive names, typed action
parameters, `$current`/`$previous`, parse-time and registry-based validation. Every slice has a spec in
[`specs/`](specs/).

| Issue | Slice | PR |
|---|---|---|
| [#30](https://github.com/STotev/RuleForge/issues/30) | Shared value comparer (`ValueComparer`/`ValueConverter`), used by `eq` | [#41](https://github.com/STotev/RuleForge/pull/41) |
| [#31](https://github.com/STotev/RuleForge/issues/31) | Parse-time structural validation (`RuleParseException`) | [#43](https://github.com/STotev/RuleForge/pull/43) |
| [#32](https://github.com/STotev/RuleForge/issues/32) | Complete engine results (`RuleEngineResult`, rule status, action identity) — HITL | [#44](https://github.com/STotev/RuleForge/pull/44) |
| [#33](https://github.com/STotev/RuleForge/issues/33) | ISO 8601 string dates for schedule `ReferencePath` | [#48](https://github.com/STotev/RuleForge/pull/48) |
| [#34](https://github.com/STotev/RuleForge/issues/34) | `OperatorContext` operator API — HITL | [#45](https://github.com/STotev/RuleForge/pull/45) |
| [#35](https://github.com/STotev/RuleForge/issues/35) | Action parameters as CLR values, `GetParameter<T>` | [#49](https://github.com/STotev/RuleForge/pull/49) |
| [#36](https://github.com/STotev/RuleForge/issues/36) | `gt`, `gte`, `lt`, `lte` | [#46](https://github.com/STotev/RuleForge/pull/46) |
| [#37](https://github.com/STotev/RuleForge/issues/37) | Case-insensitive names | [#47](https://github.com/STotev/RuleForge/pull/47) |
| [#38](https://github.com/STotev/RuleForge/issues/38) | `$current` / `$previous` scope prefixes (no migration guard: package unpublished) | [#50](https://github.com/STotev/RuleForge/pull/50) |
| [#39](https://github.com/STotev/RuleForge/issues/39) | `RuleValidator` against registered operators and actions | [#51](https://github.com/STotev/RuleForge/pull/51) |

### Tooling
CI on PRs (build + test, TRX and Cobertura artifacts), release-please + MinVer versioning with squash-merged PR
titles, central package management, issue/PR templates, Claude Code setup (`AGENTS.md`, `.claude/settings.json`).

---

## 🎯 Milestones

Agreed 2026-10-07 after settling the deployment and ownership questions in [`vision.md`](vision.md). Each milestone
becomes a PRD (or a few issues) when it starts; **0.5** is planned in PRD #66.

| Milestone | Contents | Notes |
|---|---|---|
| **0.5: JSON state & central-service spike** ([PRD #66](https://github.com/STotev/RuleForge/issues/66)) | Path resolution on `JsonElement` and `JsonNode`; ISO date strings compared as instants; `changed` via `ValueComparer`; a central rules-service sample (ASP.NET Core, JSON events with a tenant ID, scheduled actions handed to a scheduler) | The spike decides the **event envelope** and informs the scheduled-action entry point and cancellation |
| **0.6: Schema System & fluent rule builder** | Schema model in Abstractions; fluent builder with reflection-based generation from DTOs (attributes as a convention on the builder); action parameters described with the same builder; `ISchemaStore`; transport-free catalogue; schema-based path validation in `RuleValidator`; **fluent rule builder** that produces `RuleDefinition` in code (`o => o.Total` → `"Total"`), sharing the expression → path translation with the schema builder | Detail under [Schema & validation](#schema--validation) below |
| **0.7: Hosting & producer utilities** | `RuleForge.AspNetCore` with `MapRuleForge` (schemas, operators, actions, validate, schema registration); producer-side registration client; explicit state snapshotter; event envelope as decided in 0.5 | Optional packages; no rule CRUD |
| **0.8: Scheduling** | [Time semantics review](#time-semantics-review-needs-a-proper-breakdown) first; deterministic `ScheduledAction` key; cancellation/replacement semantics; scheduled-action entry point; past-time and amount validation; "dispatching scheduled actions" guide; execution observer hook | Open design questions in the vision's "Running scheduled actions later" |
| **0.9 / 1.0: Release readiness** | Benchmarks ([#27](https://github.com/STotev/RuleForge/issues/27)), XML docs, guides, NuGet package metadata, first NuGet publish | Release is cleared; publishing waits for this milestone |
| **Post-1.0** | Operations dashboard and execution store implementations; EF Core snapshotter; scheduler integration packages; typed rule consumers and dispatch, with transport adapters (e.g. MassTransit); path variables and functions; more operators | Order to be decided; see the vision's *Under consideration* |

---

## 📋 Backlog — not yet scheduled

Grouped by theme. Each item needs a decision (keep / drop / reorder) and, if kept, a PRD or issue.
**Source** points to where the idea was originally written down.

### Core API & correctness
| Item | Notes | Source |
|---|---|---|
| Pluggable value comparer (`IValueComparer`) | Deferred from #22; built-in rules are fixed for now | PRD #22 Out of Scope |
| `StopOnFirstMatch` option | Stop after the first matching rule | [old roadmap §1.1.1](archive/roadmap-2026-03.md) |
| Separate `EvaluationContext` data from path resolution (`IPathResolver`) | Testability | [analysis §6](archive/business-rules-analysis.md) |
| Path whitelisting for untrusted rules | Security, when end users author rules; comes with schema-based validation in **0.6** | [analysis §13](archive/business-rules-analysis.md) |
| Rule indexing by event type | Performance | [analysis §9](archive/business-rules-analysis.md) |

### Scheduling follow-ups (deferred from PRD #1)
| Item | Notes | Source |
|---|---|---|
| Scheduler backends (`IScheduler`; Hangfire, Quartz, background service) | RuleForge currently only computes `ScheduledFor`; integration packages are post-1.0 | PRD #1 Out of Scope |
| Timezone support | UTC-only today; decide together with the time-semantics review below (**0.8**) | PRD #1 Out of Scope |
| `BlockPastEvents` flag; validation of zero/negative `Amount` | Past dates are currently returned as-is; **0.8** | [PRD #1 plan](archive/action-scheduling-di-plan.md) |
| Cron / recurring schedules, business-day calculations | | PRD #1 Out of Scope; old roadmap Phase 4.4 |

### Time semantics review (needs a proper breakdown)
Scheduled as the first part of **0.8**. Keeping the current behaviour for now, but it needs a dedicated discussion
and design before 1.0. Raised on
2026-10-06 while reviewing #33 / PR #48.

**Today**, every date conversion goes through `ValueConverter` (#30) and assumes UTC when no time zone is known:
- `DateTime` with `Kind = Unspecified` is treated as UTC — in condition comparisons (`eq`, `gt`, …), schedule
  `ReferencePath` values (#33) and `GetParameter<DateTime/DateTimeOffset>` (#35).
- ISO 8601 strings without an offset (`"2026-05-01T09:00:00"`) are UTC, and date-only strings (`"2026-05-01"`) are
  UTC midnight.

**The risk**: `Unspecified` values are common (most databases/ORMs return them) and usually mean *local* time. In the
US, treating such a value as UTC shifts it by 4–10 hours (and by a different amount across DST changes), so a
reminder scheduled "2 hours before 09:00" fires at the wrong time, and date comparisons can land on the wrong day.
Date-only values are arguably calendar dates, not instants.

**To work out**:
- What `Unspecified` should mean: reject it (fail loudly), use a configured default time zone (engine option or
  DI), treat it as server-local time (fragile on servers), or take it from a per-field time zone in the Schema System.
- Whether date-only values should compare as calendar dates (`DateOnly`) rather than UTC midnight instants.
- How offset-less ISO strings in rules relate to that same default.
- How this interacts with **Timezone support** for schedules, and with scheduler backends that dispatch at local times.
- Migration: whichever default is chosen changes results for existing rules.

### Path variables and functions (for consideration)
Raised on 2026-10-06 while reviewing #38 / PR #50. Worth a design pass before more `$` names are added.

**Today** `$current` and `$previous` are the only variables, written as plain path prefixes, and the knowledge
of what is valid lives in three places: `EvaluationContext.Resolve`, `ChangedOperator` (which strips the scope) and the
parser's `RuleJsonValidator` (which rejects unknown `$` names). Every new variable would mean touching all three.

**Ideas to explore**:
- **One place for variables**: a single resolver/registry that the engine, `changed`, the parser and `RuleValidator`
  (#39) all consult, instead of hard-coded checks. A sign of success: `changed` no longer has to strip a
  `$current.`/`$previous.` prefix from its operand to get the bare property (#50); operators should get the property
  and pick a state through the API instead.
- **A public API to define and register variables**, e.g. `$event` (event metadata), `$rule`, `$tenant`, or
  application-specific ones, registered alongside operators and actions (and through DI, #5).
- **Functions**, e.g. `$now()`, `$today()`, `$now(Europe/London)`. Possibly with their own sigil, such as `@now(...)`,
  to tell functions from variables. They'd have to use the injected `TimeProvider`, so they stay deterministic in
  tests, and they tie directly into the **time semantics review** above.

**Questions**:
- Syntax: `$` for both, or `$` for variables and `@` for functions? Is `$current()` a function or a variable?
- Strings vs structure: parse function calls out of `path` strings (a small expression language), or add a structured
  JSON node such as `{ "function": "now", "args": ["Europe/London"] }` (a new `ConditionNode` type) that the parser
  and UIs handle without string parsing?
- Registration and discovery: how do custom variables and functions declare their arguments and return types, so
  `RuleValidator` and rule-authoring UIs (Schema System) can check and offer them?
- Safety: what custom functions may do when end users write rules, and whether results are cached per event.
- Overlap with **expression support** (e.g. `Price * 1.2 > 100`), which the March analysis also suggested
  ([analysis §7](archive/business-rules-analysis.md)); functions could be its first step or a separate feature.

### Schema & validation
Scheduled for **0.6**; the design decisions (code-first, fluent builder, runtime registration, `ISchemaStore`,
catalogue) are in [`vision.md`](vision.md#decided). Earlier sketch: [old roadmap §1.2](archive/roadmap-2026-03.md).

The schema is intended as the single source for:
- **UI form generation**: fields, types and labels for rule-authoring UIs.
- **Path validation / whitelisting**: reject unknown paths when a rule is saved. `RuleValidator` in #22 can't do this
  because it doesn't know an event's CLR type.
- **Path cache**: pre-warm or bound the shared accessor cache (#15) to schema paths, possibly as a DI registration
  option. Today the cache grows with every distinct (type, path), which is fine while rules are developer-authored.
- **Typed JSON values**: produce real dates and numbers at parse time; #22's ISO-string coercion becomes a fallback.
- **Declared-type limits**: describe payloads whose properties are declared as `object` or an interface, which path
  resolution can't traverse today (it follows declared property types).

### Event system & configuration (old Phase 1.3)
`RuleEvent` became the **event envelope**, decided in the 0.5 spike. `IRuleRepository` is dropped: rule storage is the
consumer's, and the library defines no rule source ([`vision.md`](vision.md#decided)). Still open: an in-process
`IRuleEventPublisher`, and a fluent `RuleEngineBuilder` (overlaps with #5's `RuleForgeBuilder`; reconcile before
starting). Detail: [old roadmap §1.3](archive/roadmap-2026-03.md).

### Performance & benchmarks — [#27](https://github.com/STotev/RuleForge/issues/27)
BenchmarkDotNet suite (path resolution, condition evaluation, JSON parsing, engine end-to-end, allocations),
published results in `docs/benchmarks.md`, a README Performance section, and a manual CI workflow. After #22, before 1.0.

### Packaging & documentation (old Phase 1.4)
NuGet package metadata and publishing workflow (packages are now `RuleForge.Abstractions`, `RuleForge`,
`RuleForge.Extensions.DependencyInjection`), XML docs on all public APIs, concept/operator guides,
scenario samples. Detail: [old roadmap §1.4](archive/roadmap-2026-03.md), [PRD #1 plan Phase 3](archive/action-scheduling-di-plan.md).

### Developer experience (old Phase 2)
Fluent rule builder API (scheduled for **0.6**; post-1.0 typed rule consumers build on it); more operators — comparison (`between` `in`; `gt` `gte` `lt` `lte` are in #22), string
(`contains` `startsWith` `endsWith` `matches`), collection (`any` `all` `count`), date. DI moved to #5.
Detail: [old roadmap Phase 2](archive/roadmap-2026-03.md).

A **test-helpers package** for consumers (builders for `OperatorContext`, rules and fake actions) — raised when
reviewing #34; reconsider once plugin authors exist. Source: [OperatorContext spec](specs/2026-10-05-operator-context-design.md) Q3.

### Rule management (old Phase 3)
Rule CRUD and multi-tenancy are the consumer's ([`vision.md`](vision.md#decided)). Still open for the library:
rule versioning and lifecycle (draft/active/archived) as data in the rule model, a rule simulator and an evaluation
trace. Detail: [old roadmap Phase 3](archive/roadmap-2026-03.md).

### Infrastructure & scale (old Phase 4)
Persistence and distributed events (MassTransit/RabbitMQ) are the consumer's. The execution observer hook is in
**0.8**, the catalogue REST API in **0.7**, and the operations dashboard is post-1.0. Still open: structured `ILogger`
logging (the engine reports failures only in its results today). Detail: [old roadmap Phase 4](archive/roadmap-2026-03.md).

---

## 🚫 Out of scope

Visual rule builder UI (separate project), ML/AI rule suggestions, workflow/BPM engine capabilities,
action chaining (use multiple rules).

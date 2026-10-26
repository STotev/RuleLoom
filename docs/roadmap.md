# RuleLoom Roadmap

**Last reviewed**: 2026-10-26

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
| 0.2.0 | [Released](https://github.com/STotev/RuleLoom/releases/tag/v0.2.0) 2026-10-02 | Action results, scheduling model, action factory, schedule calculation |
| 0.2.1 | [Released](https://github.com/STotev/RuleLoom/releases/tag/v0.2.1) 2026-10-03 | Nested path fix (#14), shared path cache (#15) |
| 0.3.0 | [Released](https://github.com/STotev/RuleLoom/releases/tag/v0.3.0) 2026-10-06 | PRD #22 API hardening (one breaking release; migration guide follows in #6) |
| 0.4.0 | [Released](https://github.com/STotev/RuleLoom/releases/tag/v0.4.0) 2026-10-06 | Action names by registration, `IAction`, dependency injection, upgrading guide |
| 0.5.0 | [Released](https://github.com/STotev/RuleLoom/releases/tag/v0.5.0) 2026-10-15; the first release on [NuGet](https://www.nuget.org/packages/RuleLoom) | JSON state, ISO date comparison, required rule ids, `ConsoleLogAction`, ASP.NET Core sample |
| 0.6.0 | [Released](https://github.com/STotev/RuleLoom/releases/tag/v0.6.0) 2026-10-25 | Event schemas (`RuleLoom.Schemas`), contexts, validation against schemas, the catalog, previous state from changed fields |

---

## ✅ Done

### Core foundation
Abstractions (`ConditionNode`, `IConditionOperator`, `IAction`), `RuleEngine`, `ConditionEvaluator`,
`PathResolver` with compiled-expression caching, operators `eq` `changed` `and` `or` `not`, JSON parser,
`LogAction`.

### Stability fixes (old Phase 1.1)
Priority ordering (higher first), operand-count validation, null-safe path resolution, continue-on-error
per rule, integration test project.

### Action scheduling & DI — PRD [#1](https://github.com/STotev/RuleLoom/issues/1) ✅

| Issue | Slice | PR | Spec |
|---|---|---|---|
| [#2](https://github.com/STotev/RuleLoom/issues/2) | Scheduling model & `ActionResult` | [#7](https://github.com/STotev/RuleLoom/pull/7) | [scheduling-model-abstractions](specs/2026-03-28-scheduling-model-abstractions-design.md) |
| [#3](https://github.com/STotev/RuleLoom/issues/3) | Action factory & per-rule results | [#9](https://github.com/STotev/RuleLoom/pull/9) | [action-factory-pattern](specs/2026-03-28-action-factory-pattern-design.md) |
| [#4](https://github.com/STotev/RuleLoom/issues/4) | Schedule calculation engine | [#10](https://github.com/STotev/RuleLoom/pull/10) | [schedule-calculation-engine](specs/2026-03-29-schedule-calculation-engine-design.md) |
| [#55](https://github.com/STotev/RuleLoom/issues/55) | Action names by registration, `"action"` in rule JSON | [#56](https://github.com/STotev/RuleLoom/pull/56) | [action-names](specs/2026-10-06-action-names-design.md) |
| [#5](https://github.com/STotev/RuleLoom/issues/5) | Dependency injection (`AddRuleLoom`) | [#54](https://github.com/STotev/RuleLoom/pull/54) | [dependency-injection](specs/2026-10-06-dependency-injection-design.md) |
| [#58](https://github.com/STotev/RuleLoom/issues/58) | Rename `IActionExecutor` → `IAction` | [#59](https://github.com/STotev/RuleLoom/pull/59) | — |
| [#6](https://github.com/STotev/RuleLoom/issues/6) | Integration tests, samples, [upgrading guide](guides/upgrading.md), quick start rewrite | [#60](https://github.com/STotev/RuleLoom/pull/60) | [integration-samples-upgrading](specs/2026-10-07-integration-samples-upgrading-design.md) |

Actually *dispatching* scheduled actions was out of scope for this PRD; see the vision's "Running scheduled actions
later".

### Code review bug batch (2026-10-02)
| Issue | Change | Fixed by |
|---|---|---|
| [#14](https://github.com/STotev/RuleLoom/issues/14) | Unscoped nested paths (`Address.City`) threw | [#24](https://github.com/STotev/RuleLoom/pull/24) |
| [#15](https://github.com/STotev/RuleLoom/issues/15) | Path accessor cache recreated per event (~290× faster now) | [#26](https://github.com/STotev/RuleLoom/pull/26) |
| [#20](https://github.com/STotev/RuleLoom/issues/20) | Unit tests for operators, evaluator, path resolution (`RuleLoom` coverage 57% → 90%) | [#29](https://github.com/STotev/RuleLoom/pull/29) |
| [#21](https://github.com/STotev/RuleLoom/issues/21) | Test packages aligned; xunit v3 on Microsoft.Testing.Platform; coverage in both projects | [#28](https://github.com/STotev/RuleLoom/pull/28) |
| [#16](https://github.com/STotev/RuleLoom/issues/16) · [#17](https://github.com/STotev/RuleLoom/issues/17) · [#18](https://github.com/STotev/RuleLoom/issues/18) · [#19](https://github.com/STotev/RuleLoom/issues/19) | `changed` with scoped paths · clearer errors · cancellation checks · ISO-only string dates | folded into #38 · #34 · #32 · #33 |

### API hardening — PRD [#22](https://github.com/STotev/RuleLoom/issues/22) ✅ (ships in 0.3.0)
Shared value comparison, `OperatorContext`, ordering operators, complete results, case-insensitive names, typed action
parameters, `$current`/`$previous`, parse-time and registry-based validation. Every slice has a spec in
[`specs/`](specs/).

| Issue | Slice | PR |
|---|---|---|
| [#30](https://github.com/STotev/RuleLoom/issues/30) | Shared value comparer (`ValueComparer`/`ValueConverter`), used by `eq` | [#41](https://github.com/STotev/RuleLoom/pull/41) |
| [#31](https://github.com/STotev/RuleLoom/issues/31) | Parse-time structural validation (`RuleParseException`) | [#43](https://github.com/STotev/RuleLoom/pull/43) |
| [#32](https://github.com/STotev/RuleLoom/issues/32) | Complete engine results (`RuleEngineResult`, rule status, action identity) — HITL | [#44](https://github.com/STotev/RuleLoom/pull/44) |
| [#33](https://github.com/STotev/RuleLoom/issues/33) | ISO 8601 string dates for schedule `ReferencePath` | [#48](https://github.com/STotev/RuleLoom/pull/48) |
| [#34](https://github.com/STotev/RuleLoom/issues/34) | `OperatorContext` operator API — HITL | [#45](https://github.com/STotev/RuleLoom/pull/45) |
| [#35](https://github.com/STotev/RuleLoom/issues/35) | Action parameters as CLR values, `GetParameter<T>` | [#49](https://github.com/STotev/RuleLoom/pull/49) |
| [#36](https://github.com/STotev/RuleLoom/issues/36) | `gt`, `gte`, `lt`, `lte` | [#46](https://github.com/STotev/RuleLoom/pull/46) |
| [#37](https://github.com/STotev/RuleLoom/issues/37) | Case-insensitive names | [#47](https://github.com/STotev/RuleLoom/pull/47) |
| [#38](https://github.com/STotev/RuleLoom/issues/38) | `$current` / `$previous` scope prefixes (no migration guard: package unpublished) | [#50](https://github.com/STotev/RuleLoom/pull/50) |
| [#39](https://github.com/STotev/RuleLoom/issues/39) | `RuleValidator` against registered operators and actions | [#51](https://github.com/STotev/RuleLoom/pull/51) |

### JSON state & ASP.NET Core spike — PRD [#66](https://github.com/STotev/RuleLoom/issues/66) ✅ (ships in 0.5.0)
Rules run on JSON state (`JsonElement`, `JsonNode`), ISO 8601 strings compare as dates, `changed` uses the shared
comparer, rule ids are required, and the console action is `ConsoleLogAction`. An ASP.NET Core sample receiving JSON events
from other services tried the library as intended; its [findings](specs/2026-10-14-aspnetcore-sample-design.md#findings) decided the event
envelope and shaped 0.6–0.8.

| Issue | Slice | Type | PR | Spec |
|---|---|---|---|---|
| [#68](https://github.com/STotev/RuleLoom/issues/68) | Resolve paths on JSON state (`JsonElement`, `JsonNode`) | AFK | [#72](https://github.com/STotev/RuleLoom/pull/72) | [json-state-paths](specs/2026-10-09-json-state-paths-design.md) |
| [#69](https://github.com/STotev/RuleLoom/issues/69) | ISO date strings compared as instants; `changed` via `ValueComparer` | AFK | [#75](https://github.com/STotev/RuleLoom/pull/75) | [json-value-comparison](specs/2026-10-09-json-value-comparison-design.md) |
| [#70](https://github.com/STotev/RuleLoom/issues/70) | ASP.NET Core sample (spike) | HITL | [#76](https://github.com/STotev/RuleLoom/pull/76) | [aspnetcore-sample](specs/2026-10-14-aspnetcore-sample-design.md) |
| [#71](https://github.com/STotev/RuleLoom/issues/71) | Spike findings and event envelope decision | HITL | [#82](https://github.com/STotev/RuleLoom/pull/82) | [aspnetcore-sample § Findings](specs/2026-10-14-aspnetcore-sample-design.md#findings) |
| [#77](https://github.com/STotev/RuleLoom/issues/77) | Require a rule id (spike follow-up) | AFK | [#79](https://github.com/STotev/RuleLoom/pull/79) | [required-rule-id](specs/2026-10-14-required-rule-id-design.md) |
| [#78](https://github.com/STotev/RuleLoom/issues/78) | Rename `LogAction` to `ConsoleLogAction` (spike follow-up) | AFK | [#80](https://github.com/STotev/RuleLoom/pull/80) | [console-log-action](specs/2026-10-15-console-log-action-design.md) |

### Schemas, contexts & the catalog — PRD [#87](https://github.com/STotev/RuleLoom/issues/87) ✅ (ships in 0.6.0)
Event schemas (`EventSchema<T>`, inferred from DTOs, JSON Schema 2020-12) in the dependency-free `RuleLoom.Schemas`;
**contexts** (`@customer.Tier`) declared by event schemas and loaded by context providers, once per event; previous state
rebuilt from changed fields; rule paths parsed once (`RulePath`) and validated against the event's and contexts'
schemas; and the **catalog**, the one vocabulary the engine, the validator and an authoring UI run on (`CatalogBuilder`,
`CatalogDocument`, `ICatalogStore`; decisions in [`GLOSSARY.md`](../GLOSSARY.md) and [`adr/`](adr/)). Scoped to one
application first; the ASP.NET Core sample and the [schemas and contexts guide](guides/schemas-and-contexts.md) show it
together.

| Issue | Slice | Type | Blocked by | PR | Spec |
|---|---|---|---|---|---|
| [#90](https://github.com/STotev/RuleLoom/issues/90) | Rebuild the previous state from changed fields | AFK | — | [#97](https://github.com/STotev/RuleLoom/pull/97) | [previous-from-changes](specs/2026-10-18-previous-from-changes-design.md) |
| [#91](https://github.com/STotev/RuleLoom/issues/91) | Contexts: related data loaded by context providers (`@` paths) | AFK | — | [#99](https://github.com/STotev/RuleLoom/pull/99) | [contexts](specs/2026-10-18-contexts-design.md) |
| [#92](https://github.com/STotev/RuleLoom/issues/92) | Schema builder and JSON Schema export (`RuleLoom.Schemas`) | HITL | — | [#100](https://github.com/STotev/RuleLoom/pull/100) | [schema-builder](specs/2026-10-18-schema-builder-design.md) |
| [#93](https://github.com/STotev/RuleLoom/issues/93) | Schema store and registration in `AddRuleLoom`; context references passed to providers | AFK | #92 | [#103](https://github.com/STotev/RuleLoom/pull/103) | [schema-store](specs/2026-10-19-schema-store-design.md) |
| [#101](https://github.com/STotev/RuleLoom/issues/101) | Context schemas described with their providers | AFK | #93 | [#105](https://github.com/STotev/RuleLoom/pull/105) | [context-schemas](specs/2026-10-19-context-schemas-design.md) |
| [#94](https://github.com/STotev/RuleLoom/issues/94) | Schema-aware rule validation with severities | AFK | #91, #92, #101 | [#106](https://github.com/STotev/RuleLoom/pull/106) | [schema-validation](specs/2026-10-21-schema-validation-design.md) |
| [#107](https://github.com/STotev/RuleLoom/issues/107) | Parse rule paths once and restructure RuleValidator | AFK | #94 | [#108](https://github.com/STotev/RuleLoom/pull/108) | [path-model](specs/2026-10-22-path-model-design.md) |
| [#109](https://github.com/STotev/RuleLoom/issues/109) | The catalog: one immutable registry of event schemas, context providers, operators and actions that the engine, validator and UI run on; catalog store | HITL | #107 | [#110](https://github.com/STotev/RuleLoom/pull/110) | [catalog](specs/2026-10-25-catalog-design.md) |
| [#95](https://github.com/STotev/RuleLoom/issues/95) | Sample and guide for schemas and contexts, built on the catalog | HITL | #91, #93, #94, #101, #107, #109 | [#111](https://github.com/STotev/RuleLoom/pull/111) | [schemas-contexts-sample](specs/2026-10-25-schemas-contexts-sample-design.md) |

### Tooling
CI on PRs (build + test, TRX and Cobertura artifacts), release-please + MinVer versioning with squash-merged PR
titles, central package management, issue/PR templates, Claude Code setup (`AGENTS.md`, `.claude/settings.json`).
From 0.5.0: NuGet packages published by the release workflow with trusted publishing, packed on every PR; protected
`main` (PRs and the CI check required).

---

## 🎯 Milestones

Agreed 2026-10-07 after settling the deployment and ownership questions in [`vision.md`](vision.md). Each milestone
becomes a PRD (or a few issues) when it starts. **0.5** shipped (PRD #66) and **0.6** is done (PRD #87), see *Done*;
the 0.5 spike's [findings](specs/2026-10-14-aspnetcore-sample-design.md#findings) shaped 0.7–0.8 below.

| Milestone | Contents | Notes |
|---|---|---|
| **0.7: Typed actions & authoring endpoints** ([PRD #113](https://github.com/STotev/RuleLoom/issues/113)) | **Typed actions** (`IAction<TParameters>`, parameters described by their type and `ActionDescriptor<TAction, TParameters>`, in the catalog document as JSON Schema); **paths in parameters** (`{ "$path": "@customer.Email" }`, [ADR 0003](adr/0003-paths-in-parameters-are-marked.md)); parameters validated against their schema; `RuleLoom.AspNetCore` with `MapRuleLoom` (catalog, validate); rule arrays parsed rule by rule (#81); every context declaration naming its provider | One application first; no rule CRUD. Detail below |
| **0.8: Scheduling** | [Time semantics review](#time-semantics-review-needs-a-proper-breakdown) first; **event envelope** `RuleEvent` as decided in 0.5, moved from 0.7 (`ExecuteAsync(RuleEvent, rules)` beside the existing overloads, `EvaluationContext.Event`, `$event.` paths, `Subject`, `EventId`, `changes`, JSON shape); `ScheduledAction.Key` (`{ruleId}:{actionIndex}:{subject}`); cancellation/replacement semantics; scheduled-action entry point `ExecuteScheduledAsync(scheduledAction, ruleEvent)` with a status for deferred runs; serializable scheduled actions for persistent job stores; past-time and amount validation; "dispatching scheduled actions" guide; execution observer hook | Open design questions in the vision's "Running scheduled actions later" |
| **0.9 / 1.0: Release readiness** | Benchmarks ([#27](https://github.com/STotev/RuleLoom/issues/27)), XML docs, guides, a **documentation site** built from `docs/` with more extensive examples (see below) | 1.0 marks a stable API; packages are already published from 0.5.0 |
| **Parked: service-to-service** | Schema publishing (`UseSchemaPublishing(p => p.UseHttp(...))`) and registration endpoints; storage packages (e.g. PostgreSQL); optional build-time schema generation; explicit state snapshotter; **scopes** (applications / bounded contexts, see the vision) | Monolith first (2026-10-24): one application, one catalog, event types unique per catalog. Picked up when several applications share an engine |
| **After 0.7** | **Fluent rule builder** producing `RuleDefinition` in code (`o => o.Total` → `"Total"`), sharing the schema builder's expression → path translation | Mainly for in-process users; placed once 0.7 settles |
| **Post-1.0** | Operations dashboard and execution store implementations; EF Core snapshotter; scheduler integration packages; typed rule consumers and dispatch, with transport adapters (e.g. MassTransit); path variables and functions; more operators | Order to be decided; see the vision's *Under consideration* |

### 0.7 — PRD [#113](https://github.com/STotev/RuleLoom/issues/113)

| Issue | Slice | Type | Blocked by | PR | Spec |
|---|---|---|---|---|---|
| [#81](https://github.com/STotev/RuleLoom/issues/81) | Parse rule arrays rule by rule | AFK | — | — | — |
| [#114](https://github.com/STotev/RuleLoom/issues/114) | Context declarations always name their provider | AFK | — | — | — |
| [#115](https://github.com/STotev/RuleLoom/issues/115) | Typed actions and their parameters in the catalog | AFK | — | [#121](https://github.com/STotev/RuleLoom/pull/121) | [typed-actions](specs/2026-10-26-typed-actions-design.md) |
| [#116](https://github.com/STotev/RuleLoom/issues/116) | Paths in action parameters (`$path`) | AFK | #115 | [#123](https://github.com/STotev/RuleLoom/pull/123) | [path-parameters](specs/2026-10-26-path-parameters-design.md) |
| [#117](https://github.com/STotev/RuleLoom/issues/117) | Validate action parameters against their schema | AFK | #115, #116 | — | — |
| [#118](https://github.com/STotev/RuleLoom/issues/118) | `RuleLoom.AspNetCore`: `MapRuleLoom` endpoints | AFK | #81 | — | — |
| [#119](https://github.com/STotev/RuleLoom/issues/119) | Sample and guide for typed actions and `MapRuleLoom` | HITL | #81, #114–#118 | — | — |

---

## 📋 Backlog — not yet scheduled

Grouped by theme. Each item needs a decision (keep / drop / reorder) and, if kept, a PRD or issue.
**Source** points to where the idea was originally written down.

### Core API & correctness
| Item | Notes | Source |
|---|---|---|
| Pluggable value comparer (`IValueComparer`) | Deferred from #22; built-in rules are fixed for now | PRD #22 Out of Scope |
| `StopOnFirstMatch` option | Stop after the first matching rule | old roadmap §1.1.1 |
| Separate `EvaluationContext` data from path resolution (`IPathResolver`) | Testability | analysis §6 |
| Path whitelisting for untrusted rules | Security, when end users author rules; comes with schema-based validation in **0.6** | analysis §13 |
| Rule indexing by event type | Performance | analysis §9 |
| Package layering review (before 1.0) | `RuleLoom.Abstractions` holds implementation (JSON parser, `PathResolver`, `EvaluationContext`, value comparison, `RulePath`) and references `RuleLoom.Schemas`; an abstractions package is usually the one others reference. Decide what is contract and what moves to `RuleLoom` | #107 discussion |

### Scheduling follow-ups (deferred from PRD #1)
| Item | Notes | Source |
|---|---|---|
| Scheduler backends (`IScheduler`; Hangfire, Quartz, background service) | RuleLoom currently only computes `ScheduledFor`; integration packages are post-1.0 | PRD #1 Out of Scope |
| Timezone support | UTC-only today; decide together with the time-semantics review below (**0.8**) | PRD #1 Out of Scope |
| `BlockPastEvents` flag; validation of zero/negative `Amount` | Past dates are currently returned as-is; **0.8** | PRD #1 plan |
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
- **Functions**, e.g. `#now()`, `#today()`, `#now(Europe/London)`, with the `#` sigil reserved for them in 0.6
  (`@` is taken by contexts, PRD #87). They'd have to use the injected `TimeProvider`, so they stay deterministic in
  tests, and they tie directly into the **time semantics review** above.

**Questions**:
- Syntax: settled in 0.6 — `$` for RuleLoom's variables, `@` for contexts, `#` for functions.
- Strings vs structure: parse function calls out of `path` strings (a small expression language), or add a structured
  JSON node such as `{ "function": "now", "args": ["Europe/London"] }` (a new `ConditionNode` type) that the parser
  and UIs handle without string parsing?
- Registration and discovery: how do custom variables and functions declare their arguments and return types, so
  `RuleValidator` and rule-authoring UIs (Schema System) can check and offer them?
- Safety: what custom functions may do when end users write rules, and whether results are cached per event.
- Overlap with **expression support** (e.g. `Price * 1.2 > 100`), which the March analysis also suggested
  (analysis §7); functions could be its first step or a separate feature.

### Schema & validation
**0.6** ([PRD #87](https://github.com/STotev/RuleLoom/issues/87)) delivers schemas, contexts, path validation and the
catalog (#109); typed actions and parameter validation follow in 0.7 ([PRD #113](https://github.com/STotev/RuleLoom/issues/113)). Design decisions are in [`vision.md`](vision.md#decided). Earlier
sketch: old roadmap §1.2.

The schema is intended as the single source for:
- **UI form generation**: fields, types and labels for rule-authoring UIs (read from the catalog: `catalog.ToJson()`).
- **Path validation / whitelisting**: reject unknown paths when a rule is saved (0.6). `RuleValidator` in #22 can't do this
  because it doesn't know an event's CLR type.
- **Path cache**: pre-warm or bound the shared accessor cache (#15) to schema paths, possibly as a DI registration
  option. Today the cache grows with every distinct (type, path), which is fine while rules are developer-authored.
- **Typed JSON values**: produce real dates and numbers at parse time; #22's ISO-string coercion becomes a fallback.
- **Declared-type limits**: describe payloads whose properties are declared as `object` or an interface. Path
  resolution follows their runtime values since #68, but a schema still can't know their fields from the declared type.

### Event system & configuration (old Phase 1.3)
`RuleEvent` became the **event envelope**, decided in the 0.5 spike. `IRuleRepository` is dropped: rule storage is the
consumer's, and the library defines no rule source ([`vision.md`](vision.md#decided)). Still open: an in-process
`IRuleEventPublisher`, and a fluent `RuleEngineBuilder` (overlaps with #5's `RuleLoomBuilder`; reconcile before
starting). Detail: old roadmap §1.3.

### Performance & benchmarks — [#27](https://github.com/STotev/RuleLoom/issues/27)
BenchmarkDotNet suite (path resolution, condition evaluation, JSON parsing, engine end-to-end, allocations),
published results in `docs/benchmarks.md`, a README Performance section, and a manual CI workflow. After #22, before 1.0.

### Packaging & documentation (old Phase 1.4)
XML docs on all public APIs (packages ship XML docs; missing ones outside Abstractions are suppressed until then),
concept/operator guides,
scenario samples. Detail: old roadmap §1.4, PRD #1 plan Phase 3.

**Documentation site** (raised 2026-10-19): publish the Markdown in `docs/` as a website (GitHub Pages, built by a
workflow on release), with API reference from the XML docs and more extensive examples (recipes for common setups:
inside a single service, a service shared by others, contexts, schemas, scheduling). To explore which tool fits:

| Tool | Notes |
|---|---|
| **DocFX** | .NET-native: Markdown guides plus API reference generated from the assemblies' XML docs, in one site; used by .NET libraries such as Polly |
| **MkDocs + Material** | Very popular for prose docs, simple to run, good search; no .NET API reference of its own |
| **Docusaurus / VitePress / Starlight** | Modern static-site generators (React, Vue, Astro), with versioned docs (Docusaurus) and good search; API reference needs a separate generator |

Decide before 1.0 (an early, minimal site could come sooner), and move or keep the current guides accordingly.

### Developer experience (old Phase 2)
Fluent rule builder API (scheduled for **0.6**; post-1.0 typed rule consumers build on it); more operators — comparison (`between` `in`; `gt` `gte` `lt` `lte` are in #22), string
(`contains` `startsWith` `endsWith` `matches`), collection (`any` `all` `count`), date. DI moved to #5.
Detail: old roadmap Phase 2.

A **test-helpers package** for consumers (builders for `OperatorContext`, rules and fake actions) — raised when
reviewing #34; reconsider once plugin authors exist. Source: [OperatorContext spec](specs/2026-10-05-operator-context-design.md) Q3.

### Rule management (old Phase 3)
Rule CRUD and multi-tenancy are the consumer's ([`vision.md`](vision.md#decided)). Still open for the library:
rule versioning and lifecycle (draft/active/archived) as data in the rule model, a rule simulator and an evaluation
trace. Detail: old roadmap Phase 3.

### Infrastructure & scale (old Phase 4)
Persistence and distributed events (MassTransit/RabbitMQ) are the consumer's. The execution observer hook is in
**0.8**, the catalog REST API in **0.7**, and the operations dashboard is post-1.0. Still open: structured `ILogger`
logging (the engine reports failures only in its results today). Detail: old roadmap Phase 4.

---

## 🚫 Out of scope

Visual rule builder UI (separate project), ML/AI rule suggestions, workflow/BPM engine capabilities,
action chaining (use multiple rules).

# RuleForge Roadmap

**Last reviewed**: 2026-10-02

The single overview of what has been built, what is next, and what is planned but not yet scheduled.
Details live in GitHub issues and in [`specs/`](specs/); this page links to them rather than repeating them.

**Vision**: a library-first rule engine where developers define *what's possible* (operators, actions,
events, schemas) and end users define *business logic* (JSON rules) — without code changes.

## How work is planned

**PRD → phase issues → spec → implementation** (see [`AGENTS.md`](../AGENTS.md#planning-flow)).
New work is referred to by **issue number**. The old "Phase N.M" numbers below are kept only to
connect to historical docs — they were reused inconsistently between documents.

## Releases

| Version | Status | Contents |
|---|---|---|
| 0.1.0 | Tagged `v0.1.0` (release-please bootstrap; no GitHub release or NuGet package) | Core engine, operators, JSON parser, Phase 1.1 stability fixes |
| 0.2.0 | [Released](https://github.com/STotev/RuleForge/releases/tag/v0.2.0) 2026-10-02 | Action results, scheduling model, action factory, schedule calculation |

---

## ✅ Done

### Core foundation
Abstractions (`ConditionNode`, `IConditionOperator`, `IActionExecutor`), `RuleEngine`, `ConditionEvaluator`,
`PathResolver` with compiled-expression caching, operators `eq` `changed` `and` `or` `not`, JSON parser,
`LogAction`.

### Stability fixes (old Phase 1.1)
Priority ordering (higher first), operand-count validation, null-safe path resolution, continue-on-error
per rule, integration test project. Report: [`archive/phase-1-1-summary.md`](archive/phase-1-1-summary.md).

### Action scheduling & DI — PRD [#1](https://github.com/STotev/RuleForge/issues/1) (partially done)

| Issue | Slice | PR | Spec |
|---|---|---|---|
| [#2](https://github.com/STotev/RuleForge/issues/2) | Scheduling model & `ActionResult` | [#7](https://github.com/STotev/RuleForge/pull/7) | [scheduling-model-abstractions](specs/2026-03-28-scheduling-model-abstractions-design.md) |
| [#3](https://github.com/STotev/RuleForge/issues/3) | Action factory & per-rule results | [#9](https://github.com/STotev/RuleForge/pull/9) | [action-factory-pattern](specs/2026-03-28-action-factory-pattern-design.md) |
| [#4](https://github.com/STotev/RuleForge/issues/4) | Schedule calculation engine | [#10](https://github.com/STotev/RuleForge/pull/10) | [schedule-calculation-engine](specs/2026-03-29-schedule-calculation-engine-design.md) |

### Tooling
CI on PRs (build + test), release-please + MinVer versioning, issue/PR templates.

---

## 🎯 Next

Work in this order. Findings from the 2026-10-02 code review are tracked as the bug batch and PRD #22.

### 1. Bug batch (code review)

| Issue | Change | Notes |
|---|---|---|
| [#14](https://github.com/STotev/RuleForge/issues/14) | Unscoped nested paths (`Address.City`) throw | ✅ Fixed — `RuleEngine_NestedProperties_ResolvesCorrectly` un-skipped |
| [#15](https://github.com/STotev/RuleForge/issues/15) | Path accessor cache recreated per event | ✅ Fixed — shared, thread-safe cache |
| [#20](https://github.com/STotev/RuleForge/issues/20) | Unit tests for operators, evaluator, path resolution | ✅ Done — unit coverage of `RuleForge` 57% → 90% |
| [#21](https://github.com/STotev/RuleForge/issues/21) | Align test package versions, coverage in both projects | ✅ Done — central package management, xunit v3 on Microsoft.Testing.Platform, coverage in both projects |
| [#16](https://github.com/STotev/RuleForge/issues/16) | `changed` always false for scoped paths | May be folded into #22 |
| [#17](https://github.com/STotev/RuleForge/issues/17) | Unclear errors for duplicates and non-operator operands | May be folded into #22 |
| [#18](https://github.com/STotev/RuleForge/issues/18) | Cancellation not checked between rules/actions | May be folded into #22 |
| [#19](https://github.com/STotev/RuleForge/issues/19) | String dates for schedule `ReferencePath` | Decided: keep, ISO 8601 only — implemented in #22 |

### 2. API hardening — PRD [#22](https://github.com/STotev/RuleForge/issues/22)

One breaking release before 1.0: shared value comparison (numbers, enums, ISO dates), `OperatorContext` operator API
(revised [proposal](proposals/operator-context-api-redesign.md)), `gt`/`gte`/`lt`/`lte`, complete results
(`Matched`/`NotMatched`/`Failed`, rule and action identity, `RuleEngineResult` wrapper), case-insensitive names,
normalised action parameters with `GetParameter<T>`, parse-time and registry-based validation.
Needs slicing into issues, then a spec per slice.

### 3. Finish PRD #1

| Issue | Slice | Status |
|---|---|---|
| [#5](https://github.com/STotev/RuleForge/issues/5) | DI extensions: `AddRuleForge()`, `RuleForgeBuilder`, `ServiceProviderActionExecutorFactory` | Needs a spec; after #22 (its factory must list action types). `RuleForge.Extensions.DependencyInjection` is an empty project |
| [#6](https://github.com/STotev/RuleForge/issues/6) | Integration tests, samples, migration guide, doc refresh | Blocked by #5. Includes the migration guide for #22 and updating [`guides/`](guides/) to the current API |

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
| Path whitelisting for untrusted rules | Security, when end users author rules | [analysis §13](archive/business-rules-analysis.md) |
| Rule indexing by event type | Performance | [analysis §9](archive/business-rules-analysis.md) |

### Scheduling follow-ups (deferred from PRD #1)
| Item | Notes | Source |
|---|---|---|
| Scheduler backends (`IScheduler`; Hangfire, Quartz, background service) | RuleForge currently only computes `ScheduledFor` | PRD #1 Out of Scope |
| Timezone support | UTC-only today | PRD #1 Out of Scope |
| `BlockPastEvents` flag; validation of zero/negative `Amount` | Past dates are currently returned as-is | [PRD #1 plan](archive/action-scheduling-di-plan.md) |
| Cron / recurring schedules, business-day calculations | | PRD #1 Out of Scope; old roadmap Phase 4.4 |

### Schema & validation (old Phase 1.2)
`FieldSchema` / `EventSchema` / `ISchemaProvider`, schema-based `IRuleValidator`, JSON export of schemas for
rule-authoring UIs. Detail: [old roadmap §1.2](archive/roadmap-2026-03.md).

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
`IRuleEventPublisher` + `RuleEvent`, in-process publisher, `IRuleRepository` + in-memory implementation,
fluent `RuleEngineBuilder`. The builder overlaps with #5's `RuleForgeBuilder` — reconcile before starting.
Detail: [old roadmap §1.3](archive/roadmap-2026-03.md).

### Performance & benchmarks — [#27](https://github.com/STotev/RuleForge/issues/27)
BenchmarkDotNet suite (path resolution, condition evaluation, JSON parsing, engine end-to-end, allocations),
published results in `docs/benchmarks.md`, a README Performance section, and a manual CI workflow. After #22, before 1.0.

### Packaging & documentation (old Phase 1.4)
NuGet package metadata and publishing workflow (packages are now `RuleForge.Abstractions`, `RuleForge`,
`RuleForge.Extensions.DependencyInjection`), XML docs on all public APIs, concept/operator guides,
scenario samples. Detail: [old roadmap §1.4](archive/roadmap-2026-03.md), [PRD #1 plan Phase 3](archive/action-scheduling-di-plan.md).

### Developer experience (old Phase 2)
Fluent rule builder API; more operators — comparison (`between` `in`; `gt` `gte` `lt` `lte` are in #22), string
(`contains` `startsWith` `endsWith` `matches`), collection (`any` `all` `count`), date. DI moved to #5.
Detail: [old roadmap Phase 2](archive/roadmap-2026-03.md).

### Rule management (old Phase 3)
Rule CRUD, versioning and lifecycle (draft/active/archived); rule simulator and evaluation trace;
multi-tenancy. Detail: [old roadmap Phase 3](archive/roadmap-2026-03.md).

### Infrastructure & scale (old Phase 4)
Persistence (EF Core, MongoDB, Redis), distributed events (MassTransit/RabbitMQ), observability —
structured `ILogger` logging (replacing the `Console.Error` write in `RuleEngine`), OpenTelemetry,
event-based `IActionExecutionObserver` — and a management REST API.
Detail: [old roadmap Phase 4](archive/roadmap-2026-03.md).

---

## 🚫 Out of scope

Visual rule builder UI (separate project), ML/AI rule suggestions, workflow/BPM engine capabilities,
action chaining (use multiple rules).

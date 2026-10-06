# Design: Integration Tests, Samples and Upgrading Guide

**Issue:** [#6 Phase 1.2E: Integration Tests, Samples & Migration Guide](https://github.com/STotev/RuleForge/issues/6)
**Parent PRD:** [#1 Action Scheduling & Dependency Injection Support](https://github.com/STotev/RuleForge/issues/1) (last slice)
**Date:** 2026-10-07
**Status:** Approved (AFK)

---

## Problem

PRD #1 and PRD #22 changed most public APIs, but the user-facing material lags behind: there is no migration guide,
the quick start still describes the 0.1 API (`RuleForge.Core`, `Task ExecuteAsync`, `"type"`), and the console sample
is a scratch file (commented-out experiments, a deliberately broken date, an example class nobody calls). Two of #6's
integration scenarios are not covered yet.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Migration guide scope | `docs/guides/upgrading.md`, one section per version: 0.1 → 0.2, 0.2 → 0.3, 0.3 → 0.4. Each breaking change gets a before/after snippet | #6 asked for PRD #1's three changes; 0.3.0 shipped without a guide and 0.4.0 (release PR #57, held for this) adds more. One page covers all of them |
| Quick start | Rewritten for the current API and kept short: install from source, a rule in code, a rule in JSON, results, custom operator/action, DI, validation, next steps. Detail lives in the JSON guide | The old page duplicated the JSON guide and was wrong throughout |
| Sample structure | `Program.cs` runs four small examples, each in its own file: `BasicExample` (rule in code, results), `SchedulingExample` (Delay/Advance, prints `ScheduledFor`), `JsonExample` (the former `RuleJsonParserExample`, cleaned up and wired in), `DependencyInjectionExample` (`AddRuleForge`, scoped action with an injected service) | Each example readable on its own; `dotnet run` exercises everything |
| Sample DI dependencies | The sample references `RuleForge.Extensions.DependencyInjection` and `Microsoft.Extensions.DependencyInjection` (central package management) | Needed for the DI example |
| Missing integration tests | Add to `RuleForge.Tests.Integration`: one rule with an immediate and a scheduled action (both outcomes, `ScheduledActions`), and a failing action followed by one that still runs | The other #6 scenarios already exist (priority order, skipped action conditions) |
| DI integration test | Satisfied by `RuleForge.Extensions.DependencyInjection.Tests` (#54): real container, scoped `RuleEngine`, action with an injected dependency, JSON rule end to end | Avoids adding DI packages to the core integration project for a duplicate test |
| Sample verification | A test-free check: `dotnet run` in CI is out of scope; the PR states the sample was run | Keeps CI unchanged |

## Out of scope

- NuGet packaging/publishing (backlog). The guides describe installing from source.
- Benchmarks (#27).

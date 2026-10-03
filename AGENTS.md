# RuleForge — Agent Instructions

> A .NET 10 / C# 14 library for building event-driven business rule engines with JSON-based rule authoring.

These instructions apply to every task in this repository.

## Development Workflow

### Planning flow

Work moves through **PRD → issue → spec → implementation**:

1. **PRD**: a GitHub issue labelled `enhancement` describing the feature (e.g. #1).
2. **Phase issues**: one issue per vertical slice, labelled `prd-phase`, added to the PRD as a GitHub
   **sub-issue** (`POST /repos/{owner}/{repo}/issues/{prd}/sub_issues` with the child's internal `id`, not its number).
   Ordering between issues uses GitHub **"blocked by"** links (`.../issues/{n}/dependencies/blocked_by` with `issue_id`).
   Keep the plain-text `Parent PRD: #n` and `## Blocked by` sections in issue bodies as well, so issues read
   correctly without the native links.
3. **Spec**: a design doc in `docs/specs/YYYY-MM-DD-<topic>-design.md`, written before implementation.
4. **Implementation**: a branch and PR that closes the phase issue.

When the implementation diverges from the spec or the issue's acceptance criteria, **record it before merging**: add a `## Deviations` section to the spec (what changed and why) and update the issue. Specs in `docs/specs/` are the source of truth for design decisions.

### Branches and commits

- Never work directly on `main`. Branch names follow release-please prefixes:
  `feat/`, `fix/`, `refactor/`, `test/`, `chore/`, `docs/` + short description.
- Use [Conventional Commits](https://www.conventionalcommits.org/) — release-please derives versions and
  the changelog from them. Use `feat!:` or a `BREAKING CHANGE:` footer for breaking API changes
  (pre-1.0, breaking changes are acceptable but must be marked).
- Keep commits small, focused, one logical change each. Prefer several commits over one large one.
- PRs target `main`, use `.github/PULL_REQUEST_TEMPLATE.md`, and are **squash-merged**: the PR title
  becomes the single commit on `main`, so it must be a Conventional Commit (`feat!:` if breaking).
  List breaking changes and their migration in the PR description.

## Build and Test

```bash
dotnet build                                   # entire solution (RuleForge.slnx)
dotnet test                                    # all tests
dotnet test tests/RuleForge.Tests              # unit tests
dotnet test tests/RuleForge.Tests.Integration  # integration tests
dotnet test --filter "FullyQualifiedName~ScheduleCalculatorTests"
dotnet test --collect:"XPlat Code Coverage"
```

CI (`.github/workflows/ci.yml`) runs restore, a Release build and tests on every PR to `main`.

### Before finishing a change

- Solution builds with no new warnings and all tests pass.
- Behavior changes have tests.
- No dead code, unused abstractions, or speculative extension points.
- Naming and structure match the surrounding code.

## Priorities (when unsure)

1. Correctness
2. Simplicity — no abstractions "just in case"; the simpler solution wins when in doubt
3. Consistency with existing code
4. Readability
5. Extensibility — only when justified by a planned need

Evolve the existing code rather than rewriting it; don't refactor unrelated code in the same change.

## Architecture

| Project | Path | Role |
|---|---|---|
| `RuleForge.Abstractions` | `src/RuleForge.Abstractions` | Contracts and models. **Must have no external package dependencies.** |
| `RuleForge` | `src/RuleForge` | Default implementation: engine, evaluator, operators, actions, scheduling |
| `RuleForge.Extensions.DependencyInjection` | `src/RuleForge.Extensions.DependencyInjection` | DI registration — currently an empty placeholder (issue #5) |
| `RuleForge.Tests` | `tests/RuleForge.Tests` | xUnit unit tests |
| `RuleForge.Tests.Integration` | `tests/RuleForge.Tests.Integration` | xUnit end-to-end tests |
| `RuleForge.Samples.Console` | `samples/RuleForge.Samples.Console` | Usage sample |

Namespaces follow folders: `RuleForge.Abstractions.{Action,Condition,Rule,Scheduling}` and
`RuleForge.{Engine,Evaluators,Operators,Actions,Scheduling}` (there is no `RuleForge.Core`).

Versioning is from git tags via MinVer (`Directory.Build.props`); release-please creates the tags.

### Execution flow (`RuleEngine.ExecuteAsync`)

```
(eventType, current, previous, rules) → IReadOnlyList<RuleExecutionResult>
  → filter rules by EventType, order by Priority descending (higher runs first)
  → for each rule: rule condition false → no result for that rule
    → for each action:
        action condition false            → ActionResult.Skipped
        Schedule with Mode != Immediate   → ScheduleCalculator → ActionResult.Scheduled (executor NOT called)
                                            calculation error → ActionResult.Failed
        no executor for action.Type       → ActionResult.Failed
        otherwise                         → executor.ExecuteAsync → its ActionResult (exception → Failed)
  → an exception in a rule is written to stderr and the next rule continues (fail-safe);
    OperationCanceledException always propagates
```

### Key concepts

- **Conditions** are trees of `ConditionNode`: `OperatorCondition` (operator name + operands),
  `PathOperand`, `ValueOperand`. Built-in operators: `eq`, `changed`, `and`, `or`, `not`.
- **Paths** resolve via `PathResolver` (compiled, cached expressions): `"Price"` = current,
  `"current.Price"` / `"previous.Price"` explicit. Missing properties resolve to `null`.
- **Actions** are resolved by `Type` through `IActionExecutorFactory` (`DirectActionExecutorFactory`
  for non-DI use). Built-in action: `log`.
- **Scheduling** (`ScheduleMetadata`): `Immediate` (default) / `Delay` (+offset) / `Advance` (−offset),
  in Seconds/Minutes/Hours/Days, from `ReferencePath` or the injected `TimeProvider`'s UTC now. UTC only.
  RuleForge computes `ScheduledFor`; dispatching scheduled work is the consumer's responsibility.
- **JSON**: `RuleJsonParser.Parse(json)` / `ParseMany(json)`; property names are case-insensitive.

### Extension points

- **Operator**: implement `IConditionOperator` — lowercase `Name`, `MinOperands`/`MaxOperands`
  (`int.MaxValue` = unlimited), resolve operands via `IConditionEvaluator.ResolveValue`/`Evaluate`.
- **Action**: implement `IActionExecutor` — kebab-case `Type`,
  `Task<ActionResult> ExecuteAsync(ActionDefinition, EvaluationContext, CancellationToken)`;
  return `ActionResult.Success()` or `ActionResult.Failed(...)`.

## Code Style

- `.editorconfig`: 4 spaces for C#, 2 for JSON/YAML/XML/csproj, LF line endings.
- File-scoped namespaces, nullable enabled, implicit usings.
- `var` when the type is evident; collection expressions `[]`; primary constructors where they fit.
- Seal classes that aren't designed for inheritance; expression-bodied members for simple members.
- XML doc comments on public API in `RuleForge.Abstractions`.

## Testing Conventions

- xUnit; `[Fact]` for single scenarios, `[Theory]` + `[InlineData]` for matrices.
- Test names: `MethodName_Scenario_ExpectedOutcome`.
- Deterministic: fixed UTC timestamps via a fixed `TimeProvider` (see `FixedTimeProvider` in
  `RuleEngineTests`), never `DateTime.Now`.
- Test behavior through public contracts, not implementation details.

## Plans and Docs

All documentation lives in `docs/` (index: `docs/README.md`):

- `docs/roadmap.md` — the single overview: done, next, backlog. Update it when an issue is
  created, closed, or re-scoped.
- `docs/specs/` — authoritative design specs (`YYYY-MM-DD-<topic>-design.md`), each with a
  Deviations section once implemented.
- `docs/proposals/` — designs not yet scheduled. `docs/guides/` — user-facing guides.
- `docs/archive/` — historical; don't update, and don't treat as current.
- Skills that write specs or plans elsewhere by default (e.g. `docs/superpowers/specs/`, `./plans/`)
  must write to `docs/specs/` instead.
- `CHANGELOG.md` is generated by release-please from the squash-merged PR titles. Never edit it in a
  feature PR; fix wording via the PR title, or adjust the release-please PR itself before it merges.

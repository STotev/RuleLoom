# RuleLoom — Agent Instructions

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
dotnet build                                             # entire solution (RuleLoom.slnx)
dotnet test                                              # all tests
dotnet test --project tests/RuleLoom.Tests              # unit tests
dotnet test --project tests/RuleLoom.Tests.Integration  # integration tests
dotnet test --filter-class "RuleLoom.Tests.ScheduleCalculatorTests"
dotnet test --filter-method "*Resolve_Path*"
dotnet test --coverage --coverage-output-format cobertura --results-directory ./TestResults
```

Tests use **xunit v3 on Microsoft.Testing.Platform** (opted in via `global.json`), not VSTest:
pass project paths with `--project`, and use xunit's `--filter-class` / `--filter-method` / `--filter-query`
(the VSTest `--filter "FullyQualifiedName~..."` syntax doesn't apply). Package versions live in
`Directory.Packages.props` (central package management); shared test settings in `tests/Directory.Build.props`.
Pass `TestContext.Current.CancellationToken` to methods that accept a token (analyzer xUnit1051).

CI (`.github/workflows/ci.yml`) runs restore, a Release build and tests (with TRX reports and Cobertura coverage
uploaded as artifacts) on every PR to `main`.

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
| `RuleLoom.Abstractions` | `src/RuleLoom.Abstractions` | Contracts and models. **Must have no external package dependencies.** |
| `RuleLoom` | `src/RuleLoom` | Default implementation: engine, evaluator, operators, actions, scheduling |
| `RuleLoom.Schemas` | `src/RuleLoom.Schemas` | Event schemas: `EventSchema<T>` classes, generated with System.Text.Json's `JsonSchemaExporter`, written/read as JSON Schema 2020-12 (`x-ruleloom-*`). **No dependencies at all** (not even Abstractions); producers use it without the engine; `RuleLoom` references it. `ISchemaStore` + `InMemorySchemaStore` |
| `RuleLoom.Extensions.DependencyInjection` | `src/RuleLoom.Extensions.DependencyInjection` | `services.AddRuleLoom(...)`: DI registration of engine, evaluator, actions, operators, context providers, validator, schemas (`AddSchemas`) |
| `RuleLoom.Tests` | `tests/RuleLoom.Tests` | xUnit unit tests |
| `RuleLoom.Tests.Integration` | `tests/RuleLoom.Tests.Integration` | xUnit end-to-end tests |
| `RuleLoom.Schemas.Tests` | `tests/RuleLoom.Schemas.Tests` | xUnit tests of schema building and JSON |
| `RuleLoom.Extensions.DependencyInjection.Tests` | `tests/RuleLoom.Extensions.DependencyInjection.Tests` | xUnit tests of DI registration (uses the real container) |
| `RuleLoom.Samples.Console` | `samples/RuleLoom.Samples.Console` | Usage sample |

Namespaces follow folders: `RuleLoom.Abstractions.{Action,Condition,Rule,Scheduling,Values}` and
`RuleLoom.{Engine,Evaluators,Operators,Actions,Scheduling,Validation}` (there is no `RuleLoom.Core`).

Versioning is from git tags via MinVer (`Directory.Build.props`); release-please creates the tags. Package metadata
is in `src/Directory.Build.props` (the package README is `src/PACKAGE.md`, the icon `docs/assets/icon.png`); when a
release PR merges, the `publish` job in `release-please.yml` packs, pushes to nuget.org (trusted publishing,
environment `nuget`) and attaches the packages to the GitHub release.

### Execution flow (`RuleEngine.ExecuteAsync`)

```
(eventType, current, previous, rules) → RuleEngineResult { Rules, HasFailures, ScheduledActions }
  → rules with another EventType are not included
  → order by Priority descending (higher runs first); cancellation checked before each rule and action
  → per rule (RuleExecutionResult with RuleId, RuleName, Status, Error, ActionResults):
      condition throws → Failed (Error set, no actions)   condition false → NotMatched   true → Matched
    → per action (ActionResult stamped with ActionName and ActionIndex by the engine):
        action condition throws           → Failed (only this action; later actions still run)
        action condition false            → Skipped
        Schedule with Mode != Immediate   → ScheduleCalculator → Scheduled (executor NOT called;
                                            also listed in ScheduledActions) / calculation error → Failed
        no executor for action.Type       → Failed
        otherwise                         → executor.ExecuteAsync → its ActionResult (exception/null → Failed)
  → failures are reported in the result, never written to the console;
    OperationCanceledException always propagates (partial results are discarded)
```

### Key concepts

- **Conditions** are trees of `ConditionNode`: `OperatorCondition` (operator name + operands),
  `PathOperand`, `ValueOperand`. Built-in operators: `eq`, `changed`, `and`, `or`, `not`, `gt`, `gte`, `lt`, `lte`.
- **Names ignore case** (`OrdinalIgnoreCase`): operator names, action names, event types and property path
  segments (exact-case property match wins; ambiguous case-insensitive matches resolve to `null`). Registering
  operators or action names that differ only in case throws. String values stay case-sensitive.
- **Paths** resolve via `PathResolver` (compiled, cached expressions): `"Price"` = current,
  `"$current.Price"` / `"$previous.Price"` explicit; operators pick a state the same way (`ctx.Resolve("$previous.Price")`).
  Other `$` names are reserved (the parser rejects them); `current.`/`previous.` without `$` are plain property names.
  Missing properties resolve to `null`. A cached entry compiles the longest prefix resolvable on declared types; the
  rest continues on the runtime value: JSON state (`JsonElement`/`JsonNode`, walked by the internal `JsonPathWalker`,
  leaves mapped to `string`/`bool`/`long`/`decimal`/`double`, objects and arrays returned as JSON) or a more specific
  CLR type (properties declared as `object`/interfaces/base types). A `PreviousState.FromChanges(current, changes)` passed
  as the previous state is an overlay: paths are looked up in the JSON `changes` (old values of changed fields) first,
  then in `current`. `#` paths are reserved for functions.
- **Contexts**: `@name.Path` reads related data loaded by an `IContextProvider` registered under `name`
  (`IContextProviderFactory`: `DirectContextProviderFactory` or DI's `AddContextProvider<T>(name)`, Scoped). The
  engine prefetches the contexts its rules use (conditions, action conditions, schedule reference paths; collected by
  `ContextReferences`) once per event, in parallel, into the `EvaluationContext` cache; actions use
  `GetContextAsync(name)`. A failed load throws `ContextLoadException` when resolved, failing only the rules that use it.
  Contexts have no previous state (`$previous.@x` is a parse error; `changed` on a context throws).
- **Actions** are referenced in rules as `"action": "<name>"` (`ActionDefinition.Action`) and resolved by name through
  `IActionFactory` (`DirectActionFactory` for non-DI use), which also lists its `ActionNames`. Names
  belong to the registration: `ActionNames.GetDefaultName(type)` gives `[ActionName]` or the class name minus an
  `Action` suffix in kebab-case; `Add(executor, name)` overrides it. Built-in action: `console-log` (`ConsoleLogAction`, for development; not registered by `AddRuleLoom`).
- **DI** (`AddRuleLoom`): one shared registry per service collection, so it can be called repeatedly. Operators
  and `IConditionEvaluator` are Singleton (built-ins included; `AddOperator`/`RemoveOperator`/`RemoveBuiltInOperators` apply
  in call order); actions, `IActionFactory` (`ServiceProviderActionFactory`), `RuleEngine` and
  `RuleValidator` are Scoped. `AddAction<T>(name?)` registers under the default or given name.
- **Validation** has two layers: the parser checks a rule's JSON shape (`RuleParseException`), and
  `RuleValidator` (`RuleLoom.Validation`) checks a rule against the registered operators and actions
  (unknown names, operand counts, incomplete schedules), returning every error with a JSONPath location.
  Property paths aren't validated (needs the Schema System).
- **Scheduling** (`ScheduleMetadata`): `Immediate` (default) / `Delay` (+offset) / `Advance` (−offset),
  in Seconds/Minutes/Hours/Days, from `ReferencePath` or the injected `TimeProvider`'s UTC now. UTC only.
  `ReferencePath` values convert via `ValueConverter` (`DateTime`/`DateTimeOffset`, or strict ISO 8601 strings).
  RuleLoom computes `ScheduledFor`; dispatching scheduled work is the consumer's responsibility.
- **Value comparison**: `ValueComparer` (`AreEqual`, `TryCompare`) and `ValueConverter` (`TryConvert`) in
  `RuleLoom.Abstractions.Values` are the single implementation of the comparison rules (numbers by value across
  types, enums by name or number, dates as UTC instants incl. strict ISO 8601 strings (two ISO strings compare as instants; ISO vs other text is
  incomparable), other strings ordinal, JSON objects/arrays by content via `DeepEquals`; `changed` uses `AreEqual` too). Operators
  and other features must use them rather than `object.Equals` or ad-hoc parsing.
- **Schemas** (`RuleLoom.Schemas`): `class XSchema : EventSchema<TDto>` configures `Event(name)` (required),
  `Field(o => o.A.B)` (`Label`/`Description`/`Ignore`) and `Context(name)` (a relation: `ReferencedBy(o => o.Id)` or
  `o => new { o.A, o.B }`; context fields are described with their provider); `Build(options)`
  generates an `EventSchemaDefinition` from how `TDto` is serialized (Web defaults unless options are given; numbers
  not typed as strings; null-oblivious types non-nullable), adds configured titles and descriptions (no automatic labels) and
  `x-ruleloom-event`/`x-ruleloom-contexts` (with `x-ruleloom-referenced-by`). `ToJson()`/`Parse` round-trip;
  field names are JSON names. `class XSchema : ContextSchema<TDto>` describes a context's fields (`Context(providerName)`
  required, `Many()`, `Field(...)`) as a `ContextSchemaDefinition` (`x-ruleloom-context`). `SchemaCollection` gathers
  both kinds (assembly scan) into a `SchemaSet` (`Events`, `Contexts`; `ToJson()` one array, `SchemaSet.Parse`).
  `ISchemaStore` keeps both: `Get/List/SaveEventAsync`, `Get/List/SaveContextAsync`.
  `ISchemaStore` (+ `InMemorySchemaStore`) keeps schemas where the engine runs; DI's `AddSchemas(s => s.Add<T>() /
  AddFromAssemblyOf<T>() / AddJson(json) / UseStore<T>())` registers sources and storage separately: the in-memory store
  starts with the sources; your own store gets them at startup (`SchemaSourcesStartup` hosted service, or
  `serviceProvider.SaveRuleLoomSchemasAsync()` without a host). `RuleLoom` references `RuleLoom.Schemas`:
  `RuleEngine` takes an optional `ISchemaStore` and wraps the context providers (`SchemaReferencingProviders`): the
  event's schema picks the provider (`Context("toAccount").LoadedBy("account")`, else the context name) and its declared
  reference is resolved on the current state and passed as `ContextRequest.Reference`
  (`GetReference<T>()`); null without one. Storage is RuleLoom-provided (in-memory now; publishing and storage packages
  in 0.7).
- **JSON**: `RuleJsonParser.Parse(json)` / `ParseMany(json)`; property names are case-insensitive. The JSON shape is
  validated before deserialization (`RuleJsonValidator`); invalid rules throw `RuleParseException` (a `JsonException`)
  whose `Path` is the JSONPath of the problem. `Id`, `Name`, `EventType` and `Condition` are required, and every condition
  root (rule or action) must be an operator.

### Extension points

- **Operator**: implement `IConditionOperator` — lowercase `Name`, `MinOperands`/`MaxOperands`
  (`int.MaxValue` = unlimited), `bool Evaluate(OperatorContext ctx)`. Read operands via `ctx.GetValue(i)` (value),
  `ctx.Test(i)` (condition, lazy; bool paths/values allowed), `ctx.GetPath(i)` (path string); compare with
  `ctx.AreEqual`/`ctx.TryCompare`. Unit-test operators with `new OperatorContext(condition, evaluationContext)`
  (pass an evaluator only when operands are nested operators).
- **Action**: implement `IAction` —
  `Task<ActionResult> ExecuteAsync(ActionDefinition, EvaluationContext, CancellationToken)`; name it by its class
  (`SendEmailAction` → `send-email`) or `[ActionName("...")]`; read parameters with
  `action.GetParameter<T>(name)` / `TryGetParameter<T>` (plain CLR values, converted via `ValueConverter`);
  return `ActionResult.Success()` or `ActionResult.Failed(...)`.
- **Context provider**: derive from `ContextProvider<TContext>` (typed `LoadAsync`) or implement `IContextProvider`
  (`ValueTask<object?> GetAsync(ContextRequest, CancellationToken)`); read the subject with `request.Resolve(path)`;
  return the context, a collection, or `null` (not found). Actions read contexts with `GetContextAsync<T>(name)`.

## Code Style

- `.editorconfig`: 4 spaces for C#, 2 for JSON/YAML/XML/csproj, LF line endings.
- File-scoped namespaces, nullable enabled, implicit usings.
- `var` when the type is evident; collection expressions `[]`; primary constructors where they fit.
- Seal classes that aren't designed for inheritance; expression-bodied members for simple members.
- XML doc comments on public API in `RuleLoom.Abstractions`.

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
- Skills that write specs or plans elsewhere by default (e.g. `docs/superpowers/specs/`, `./plans/`)
  must write to `docs/specs/` instead.
- `CHANGELOG.md` is generated by release-please from the squash-merged PR titles. Never edit it in a
  feature PR; fix wording via the PR title, or adjust the release-please PR itself before it merges.

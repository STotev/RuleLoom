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
| `RuleLoom.Abstractions` | `src/RuleLoom.Abstractions` | Contracts and models: what the engine runs (operators, actions, context providers), the `Catalog` and its registrations, action descriptors, the factory contracts. **No dependencies outside `RuleLoom.Schemas` and .NET** |
| `RuleLoom` | `src/RuleLoom` | Default implementation: `CatalogBuilder`, engine, evaluator, operators, actions, direct factories, scheduling, validation |
| `RuleLoom.Schemas` | `src/RuleLoom.Schemas` | Descriptions: event schemas (`EventSchema<T>` classes), context schemas (`ContextSchema<T>`) and action parameters (`ParametersSchema<T>`), generated with System.Text.Json's `JsonSchemaExporter`, written/read as JSON Schema 2020-12 (`x-ruleloom-*`); the catalog document and store (`CatalogDocument`, `ICatalogStore` + `InMemoryCatalogStore`). **No dependencies at all** (not even Abstractions); producers and store packages use it without the engine; `RuleLoom.Abstractions` and `RuleLoom` reference it |
| `RuleLoom.Extensions.DependencyInjection` | `src/RuleLoom.Extensions.DependencyInjection` | `services.AddRuleLoom(r => r.AddCatalog(...).UseStore<T>())`: the catalog (built at startup), engine, validator and factories |
| `RuleLoom.Tests` | `tests/RuleLoom.Tests` | xUnit unit tests |
| `RuleLoom.Tests.Integration` | `tests/RuleLoom.Tests.Integration` | xUnit end-to-end tests |
| `RuleLoom.Schemas.Tests` | `tests/RuleLoom.Schemas.Tests` | xUnit tests of schema building and JSON |
| `RuleLoom.Extensions.DependencyInjection.Tests` | `tests/RuleLoom.Extensions.DependencyInjection.Tests` | xUnit tests of DI registration (uses the real container) |
| `RuleLoom.Samples.Console` | `samples/RuleLoom.Samples.Console` | Usage sample |
| `RuleLoom.Samples.AspNetCore` | `samples/RuleLoom.Samples.AspNetCore` | Web sample: per-tenant JSON rules, contexts, the catalog document (`GET /catalog`), Quartz for scheduled actions |

Namespaces follow folders: `RuleLoom.Abstractions.{Action,Condition,Context,Rule,Scheduling,Values}` and
`RuleLoom.{Engine,Evaluators,Operators,Actions,Contexts,Scheduling,Validation}` (there is no `RuleLoom.Core`);
`Catalog` (in `RuleLoom.Abstractions`) and `CatalogBuilder` (in `RuleLoom`) sit at the roots.

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
        action name not in the catalog    → Failed
        otherwise                         → IActionFactory.Create(registration).ExecuteAsync → its ActionResult
                                            (factory or action exception / null result → Failed)
  → event types not in the catalog still run (the catalog describes; it doesn't gate execution)
  → failures are reported in the result, never written to the console;
    OperationCanceledException always propagates (partial results are discarded)
```

### Key concepts

- **Catalog** (`Catalog`, glossary in `GLOSSARY.md`, decisions in `docs/adr/`): the immutable vocabulary rules are
  written in: event schemas (with the contexts they declare), context provider registrations (name, type, schema),
  operator instances and action registrations (name, type, label, description). Built by `CatalogBuilder`
  (`Build()`, or `BuildAsync(ICatalogStore)`: code plus the store's event/context schemas, code wins, saved back with
  upserts); the `Catalog` constructor enforces the invariants: broken links in code throw (duplicate names, unusable
  operator/action names, a declared context without a provider, a `ReferencedBy` path that isn't a field), the same in
  stored entries become `Problems` that fail only the rules using them. `TryGetEvent/ContextProvider/Operator/Action`,
  `TryGetContext(eventType, name)` (only declared contexts), `ToDocument()`/`ToJson()` for UIs and stores.
  Registration is separate from invocation: the engine finds names in the catalog and asks an `IActionFactory` /
  `IContextProviderFactory` to `Create(registration)`; factories never resolve names.
- **Conditions** are trees of `ConditionNode`: `OperatorCondition` (operator name + operands),
  `PathOperand`, `ValueOperand`. Built-in operators: `eq`, `changed`, `and`, `or`, `not`, `gt`, `gte`, `lt`, `lte`.
- **Names ignore case** (`OrdinalIgnoreCase`): operator names, action names, event types and property path
  segments (exact-case property match wins; ambiguous case-insensitive matches resolve to `null`). Operator, action,
  provider or event names that differ only in case fail the catalog's build. String values stay case-sensitive.
- **Paths** are parsed once by `RulePath` (`Root` Current/Previous/Context, `Context`, `Property`; cached; the only
  place that knows the syntax: never split path strings elsewhere) and resolve via `PathResolver` (compiled, cached
  expressions): `"Price"` = current,
  `"$current.Price"` / `"$previous.Price"` explicit; operators pick a state the same way (`ctx.Resolve("$previous.Price")`).
  Other `$` names are reserved (the parser rejects them); `current.`/`previous.` without `$` are plain property names.
  Missing properties resolve to `null`. A cached entry compiles the longest prefix resolvable on declared types; the
  rest continues on the runtime value: JSON state (`JsonElement`/`JsonNode`, walked by the internal `JsonPathWalker`,
  leaves mapped to `string`/`bool`/`long`/`decimal`/`double`, objects and arrays returned as JSON) or a more specific
  CLR type (properties declared as `object`/interfaces/base types). A `PreviousState.FromChanges(current, changes)` passed
  as the previous state is an overlay: paths are looked up in the JSON `changes` (old values of changed fields) first,
  then in `current`. `#` paths are reserved for functions.
- **Contexts**: `@name.Path` reads related data the event's schema declares (`Context("name")`, loaded by the provider
  registered under `LoadedBy(...)` or else `name`; `CatalogBuilder.AddContextProvider<T>(name)`). Undeclared contexts,
  and contexts on event types not in the catalog, fail the rules that use them. The `EvaluationContext` resolves the
  declared `ReferencedBy` on the current state and passes it as `ContextRequest.Reference` (`GetReference<T>()`; null
  without one); providers are created per load by the `IContextProviderFactory`. The engine prefetches the contexts its rules use (conditions, action conditions, schedule reference paths, `$path` parameters; collected by
  `ContextReferences`) once per event, in parallel, into the `EvaluationContext` cache; actions use
  `GetContextAsync(name)`. A failed load throws `ContextLoadException` when resolved, failing only the rules that use it.
  Contexts have no previous state (`$previous.@x` is a parse error; `changed` on a context throws).
- **Actions** are referenced in rules as `"action": "<name>"` (`ActionDefinition.Action`), found in the catalog and
  created by an `IActionFactory` (`DirectActionFactory` for non-DI use: added instances by type, else a parameterless
  constructor). Names belong to the registration: `ActionNames.GetDefaultName(type)` gives `[ActionName]` or the class
  name minus an `Action` suffix in kebab-case; `AddAction<T>(name)` overrides it. Built-in action: `console-log`
  (`ConsoleLogAction`, for development; not added by default).
- **DI** (`AddRuleLoom`): one shared `CatalogBuilder` per service collection (`AddCatalog(c => ...)`, callable
  repeatedly); `UseStore<T>()` (Scoped) / `UseStore(instance)`. The catalog is built at startup by a hosted service (or
  `serviceProvider.StartRuleLoomAsync()` without a host) into an internal holder; `Catalog`, `IActionFactory` /
  `IContextProviderFactory` (`ActivatorUtilities.GetServiceOrCreateInstance`: registered services as registered, others
  created with scope dependencies and not disposed by the scope), `RuleEngine` and `RuleValidator` are Scoped;
  resolving them before startup throws.
- **Validation** has two layers: the parser checks a rule's JSON shape (`RuleParseException`), and
  `RuleValidator(catalog).Validate` (`RuleLoom.Validation`; one `RuleWalker` pass handing each node to small
  `RuleCheck`s) checks a rule against the catalog: event type not in the catalog, unknown operators/actions, operand
  counts, incomplete schedules, and paths against the event's and contexts' schemas (`SchemaPathCheck`; operators get
  their operands' fields): unknown fields/contexts, undeclared or broken contexts, paths into lists → `Errors`;
  comparisons of built-in operators that can't match and contexts without a schema → `Warnings` (`IsValid` ignores
  warnings). Every problem comes with a JSONPath location.
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
  `o => new { o.A, o.B }`; `LoadedBy(provider)`); `Build(options)`
  generates an `EventSchemaDefinition` from how `TDto` is serialized (Web defaults unless options are given; numbers
  not typed as strings; null-oblivious types non-nullable), adds configured titles and descriptions (no automatic labels) and
  `x-ruleloom-event`/`x-ruleloom-contexts` (with `x-ruleloom-referenced-by`). `ToJson()`/`Parse` round-trip;
  field names are JSON names. Context data is described by its type: `class XSchema : ContextSchema<T>` (constructor:
  `Label`/`Description`/`Field(...)`) builds a `ContextSchemaDefinition` under the provider's name
  (`x-ruleloom-context`; `IsMany` for collections); both inherit `SchemaDefinition` (`Label`, `Description`, `Fields`,
  `ToJson`). An action's parameters type is described by `ParametersSchema<T>` (`Parameter(...)`, used by typed
  descriptors) as a `ParametersSchemaDefinition` (JSON Schema with `required`; `ActionRegistration.Parameters`,
  `CatalogAction.Parameters`, `"parameters"` in the document). Schemas describe data; descriptors
  (`ActionDescriptor<TAction>`, `ActionDescriptor<TAction, TParameters>`) describe code. `CatalogBuilder` finds
  descriptions next to what they describe (the action's / provider's / data type's assembly, or `AddFromAssemblyOf`;
  public types only; two for one type fail the build), or takes them explicitly (`AddAction<T, TDescriptor>`,
  `AddContextProvider<T, TSchema>`). `SchemaCollection` gathers event schema classes (assembly scan) into a
  `CatalogDocument` holding only events, without the engine (producers). `CatalogDocument` (`Events`, `Contexts`,
  `Operators`, `Actions`, `Problems`; every section optional; `ToJson`/`Parse`) is the format of stores, UIs and later
  publishing; `ICatalogStore` (`LoadAsync`, `SaveAsync` upserting per entry, never deleting; `InMemoryCatalogStore`).
- **JSON**: `RuleJsonParser.Parse(json)` / `ParseMany(json)`; property names are case-insensitive. The JSON shape is
  validated before deserialization (`RuleJsonValidator`); invalid rules throw `RuleParseException` (a `JsonException`)
  whose `Path` is the JSONPath of the problem. `Id`, `Name`, `EventType` and `Condition` are required, and every condition
  root (rule or action) must be an operator. A parameter `{ "$path": "..." }` (its only key; ADR 0003) is parsed into a
  `RulePath` in `ActionDefinition.Parameters`; `$path` with other keys, or inside a parameter's value, is a parse error;
  any other JSON is a value.

### Extension points

- **Operator**: implement `IConditionOperator` — lowercase `Name`, `MinOperands`/`MaxOperands`
  (`int.MaxValue` = unlimited), `bool Evaluate(OperatorContext ctx)`. Read operands via `ctx.GetValue(i)` (value),
  `ctx.Test(i)` (condition, lazy; bool paths/values allowed), `ctx.GetPath(i)` (path string; parse with
  `RulePath.Parse`, resolve with `ctx.Resolve(path)`); compare with
  `ctx.AreEqual`/`ctx.TryCompare`. Unit-test operators with `new OperatorContext(condition, evaluationContext)`
  (pass an evaluator only when operands are nested operators). Optionally override
  `ValidateOperands(OperandValidationContext)` to check operands when rules are validated (`GetPath`, `GetField` with
  schemas, `Error`/`Warning`); share the rules with `Evaluate`. The validator never names specific operators: put
  operator-specific checks on the operator (built-ins do: `changed`, the comparisons via `ComparedOperands`).
- **Action**: implement `IAction<TParameters>` —
  `ExecuteAsync(TParameters, ActionDefinition, EvaluationContext, CancellationToken)`; a default interface member
  implements `IAction.ExecuteAsync` with `await action.GetParametersAsync<TParameters>(context, ct)` (paths resolved, loading
  their contexts if needed, e.g. for scheduled actions run outside the engine; then JSON binding with
  `ParametersSchemaDefinition.JsonOptions`: names ignore case, enums by name, constructor parameters without a default
  and `required` members required; a failure throws `InvalidOperationException` naming the parameter, failing only that
  action). The catalog describes the parameters from the type; an action implementing two parameter types fails the
  build. Or implement `IAction` — `ExecuteAsync(ActionDefinition, EvaluationContext, CancellationToken)` — and read
  parameters with `action.GetParameterAsync<T>(name, context)` (resolves a path) or `GetParameter<T>(name)` /
  `TryGetParameter<T>` (values only; plain CLR values, converted via `ValueConverter`); its parameters aren't described. Name it by its class (`SendEmailAction` → `send-email`) or
  `[ActionName("...")]`; return `ActionResult.Success()` or `ActionResult.Failed(...)`.
- **Action descriptor**: `class XDescriptor : ActionDescriptor<XAction, XParameters>` (constructor: `Label`,
  `Description`, `Parameter(p => p.X)` with `Label`/`Description`/`Ignore`) or `ActionDescriptor<XAction>` (no
  parameters), shipped next to the action.
- **Context provider**: derive from `ContextProvider<TContext>` / `CollectionContextProvider<TItem>` (typed `LoadAsync`;
  the type gives the schema, a `ContextSchema<T>` adds labels and ignores) or implement `IContextProvider`
  (`ValueTask<object?> GetAsync(ContextRequest, CancellationToken)`; no schema unless the store holds one); read the
  declared reference with `request.GetReference<T>()` or the subject with `request.Resolve(path)`; return the context, a
  collection, or `null` (not found). Actions read contexts with `GetContextAsync<T>(name)`.
- **Factories**: implement `IActionFactory` / `IContextProviderFactory` (`Create(registration)`) to create instances
  another way (e.g. another container).
- **Catalog store**: implement `ICatalogStore` (in `RuleLoom.Schemas`); `SaveAsync` must upsert per entry and never
  delete.

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
- `GLOSSARY.md` (repo root) — the domain language, one meaning per term; `docs/adr/` — short records of hard-to-reverse
  decisions. Use the glossary's terms in code, docs and issues.
- Skills that write specs or plans elsewhere by default (e.g. `docs/superpowers/specs/`, `./plans/`)
  must write to `docs/specs/` instead.
- `CHANGELOG.md` is generated by release-please from the squash-merged PR titles. Never edit it in a
  feature PR; fix wording via the PR title, or adjust the release-please PR itself before it merges.

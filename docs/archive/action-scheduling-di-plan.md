# Plan: Action Scheduling & Observability

> Source PRD: [GitHub Issue #1](https://github.com/STotev/RuleForge/issues/1)

## Context

This plan focuses on extending RuleForge's action model to support scheduling metadata. The library is in pre-release stage, so we're building the **scheduling model** (WHEN actions should run) without implementing the actual **scheduler** (HOW to execute them at that time). Scheduler implementation (Hangfire, Quartz, etc.) and Dependency Injection support are deferred to future phases.

## Architectural Decisions

Durable decisions that apply across all phases:

- **Three-Layer Architecture**: 
  - `RuleForge.Abstractions` - Zero-dependency contracts (MUST remain dependency-free)
  - `RuleForge.Core` - Default implementations
  - `RuleForge.Extensions.DependencyInjection` - Optional DI helpers (deferred to Phase 5+)

- **Scheduling Model (Not Implementation)**:
  - Three modes: `Immediate` (execute now), `Delay` (after reference), `Advance` (before reference)
  - Four units: `Seconds`, `Minutes`, `Hours`, `Days`
  - Reference time: Either event reception time (provided as parameter) or extracted from domain object via `PathResolver`
  - **UTC-only** (timezone support deferred to Phase 3.x+)
  - **No actual scheduler**: Phase 2 builds the model; actual execution deferred to later phases

- **JSON-First Design**:
  - All scheduling metadata serializable in JSON rule definitions
  - Use `RuleJsonParser` for serialization/deserialization
  - **Case-insensitive** parsing (mode, unit values)

- **Observability Model**:
  - Actions return `ActionResult` (not void)
  - `RuleEngine.ExecuteAsync()` returns `RuleExecutionResult` with aggregated action results
  - Statuses: `ExecutedImmediately`, `Scheduled`, `Skipped`, `Failed`

- **Prerelease Behavior**:
  - Actions with `Schedule.Mode = Delay | Advance` throw `NotSupportedException`
  - Message: "RuleForge doesn't yet support scheduling. This feature is planned for a future release."
  - Actions with `Schedule.Mode = Immediate` or `Schedule = null` execute normally

- **Breaking Changes Acceptable**:
  - Pre-v1.0 stage allows breaking changes for better design
  - Migration documentation required for each breaking change

---

## Phase 1: Action Observability

**User stories**: #7, #8, #11, #18 (from PRD)

### What to Build

Add observability to action execution without changing any scheduling behavior. Actions return `ActionResult` indicating success, failure, or skipped status. `RuleEngine.ExecuteAsync()` collects all action results and returns them in a `RuleExecutionResult` wrapper.

This phase establishes the foundation for scheduling by creating the result model, but **no scheduling metadata** is added yet.

**Key changes**:
- Create `ActionResult` class with Status, Message, Error properties
- Create `ActionExecutionStatus` enum: `ExecutedImmediately`, `Scheduled`, `Skipped`, `Failed`
- Update `IActionExecutor.ExecuteAsync()` to return `Task<ActionResult>` (**breaking change**)
- Update `RuleEngine.ExecuteAsync()` to return `Task<RuleExecutionResult>` (**breaking change**)
- Update `LogAction` to return `ActionResult.Success()`
- All existing functionality works identically, just with structured return values

**End-to-end flow**: Define rule in JSON → parse → execute → receive `RuleExecutionResult` showing which actions succeeded/failed/skipped.

### Acceptance Criteria

- [ ] `ActionResult` class exists in `RuleForge.Abstractions/Action/ActionResult.cs`
- [ ] `ActionResult` has properties: `Status`, `Message`, `Error`, `ScheduledFor` (nullable, for Phase 2)
- [ ] `ActionExecutionStatus` enum exists with: `ExecutedImmediately`, `Scheduled`, `Skipped`, `Failed`
- [ ] `ActionResult` has static factory methods: `Success(string? message = null)`, `Scheduled(DateTime scheduledFor, string? message = null)`, `Failed(Exception error)`, `Skipped(string? message = null)`
- [ ] `IActionExecutor.ExecuteAsync()` signature changed to `Task<ActionResult>`
- [ ] `RuleExecutionResult` class exists in `RuleForge.Core/Engine/` with `ActionResults` collection property
- [ ] `RuleEngine.ExecuteAsync()` returns `Task<RuleExecutionResult>` with all action outcomes
- [ ] `LogAction` updated to return `ActionResult.Success()`
- [ ] Action condition failures result in `ActionResult.Skipped()`
- [ ] Exceptions in action execution caught and wrapped in `ActionResult.Failed()`
- [ ] Unit tests for `ActionResult` factory methods
- [ ] Unit tests for `RuleExecutionResult` aggregation
- [ ] Integration test: Rule with multiple actions returns correct result collection
- [ ] Integration test: Action with condition that fails returns Skipped status
- [ ] Integration test: Action that throws exception returns Failed status
- [ ] All existing integration tests updated to handle new return type
- [ ] Migration documentation in `docs/MIGRATION.md` (create if doesn't exist)

---

## Phase 2: Scheduling Model

**User stories**: #1, #2, #3, #4, #12, #15, #16, #17 (from PRD)

### What to Build

Extend the action model to support scheduling metadata. This phase defines **WHEN** actions should run (Immediate, Delay, Advance) but does NOT implement actual scheduler execution. Actions with Delay/Advance modes throw `NotSupportedException` since scheduler implementation is deferred.

**Key changes**:
- Create `ScheduleMetadata` class: Mode, Amount, Unit, ReferencePath
- Create `ScheduleMode` enum: `Immediate`, `Delay`, `Advance`
- Create `ScheduleUnit` enum: `Seconds`, `Minutes`, `Hours`, `Days`
- Add `Schedule` property (nullable) to `ActionDefinition`
- Create `ScheduleCalculator` static utility class for date arithmetic
- Update `RuleJsonParser` to deserialize Schedule property (case-insensitive)
- **Runtime behavior**: If `Schedule.Mode != Immediate`, throw `NotSupportedException` with message "RuleForge doesn't yet support scheduling. This feature is planned for a future release."

**Reference Path Handling**:
- If `ReferencePath` is provided, use `PathResolver` to extract DateTime from domain object
- If path is invalid/missing/wrong type, fall back to `DateTime(1970, 1, 1)` (treated as past = immediate)
- If `ReferencePath` is null, use provided reference time (event time)

**JSON Defaults**:
- `mode`: "Immediate" (if omitted)
- `unit`: "Seconds" (if omitted)
- `referencePath`: null (if omitted, uses event time)

**End-to-end flow**: Define rule with schedule metadata → parse → RuleEngine checks schedule → throws NotSupportedException if not Immediate.

### Acceptance Criteria

**Model & Enums**:
- [ ] `ScheduleMetadata` class exists in `RuleForge.Abstractions/Action/ScheduleMetadata.cs`
- [ ] `ScheduleMetadata` properties: `Mode` (ScheduleMode), `Amount` (int), `Unit` (ScheduleUnit), `ReferencePath` (string?, nullable)
- [ ] `ScheduleMode` enum exists with: `Immediate`, `Delay`, `Advance`
- [ ] `ScheduleUnit` enum exists with: `Seconds`, `Minutes`, `Hours`, `Days`
- [ ] `ActionDefinition.Schedule` property added (ScheduleMetadata?, nullable)

**Schedule Calculation**:
- [ ] `ScheduleCalculator` static class exists in `RuleForge.Core/Scheduling/`
- [ ] `ScheduleCalculator.Calculate(ScheduleMetadata, EvaluationContext, DateTime referenceTime)` method exists
- [ ] Returns `DateTime?` (null if Immediate, calculated time otherwise)
- [ ] Handles `Mode = Immediate` → returns null
- [ ] Handles `Mode = Delay` with all units (Seconds, Minutes, Hours, Days)
- [ ] Handles `Mode = Advance` with all units
- [ ] Uses `referenceTime` parameter when `ReferencePath` is null
- [ ] Uses `PathResolver` to extract DateTime from `ReferencePath` when provided
- [ ] Falls back to `new DateTime(1970, 1, 1)` if ReferencePath is invalid/missing/wrong type

**JSON Parsing**:
- [ ] `RuleJsonParser` deserializes `Schedule` property correctly
- [ ] Mode/Unit parsing is **case-insensitive** ("delay", "Delay", "DELAY" all work)
- [ ] Defaults applied: mode=Immediate, unit=Seconds, referencePath=null
- [ ] JSON round-trip test: Serialize and deserialize rule with Schedule

**RuleEngine Integration**:
- [ ] `RuleEngine` checks `action.Schedule?.Mode` before execution
- [ ] If `Mode = Delay | Advance`, throws `NotSupportedException` with message: "RuleForge doesn't yet support scheduling. This feature is planned for a future release."
- [ ] If `Mode = Immediate` or `Schedule = null`, executes action normally
- [ ] Exception includes action type in message for debugging

**Unit Tests**:
- [ ] ScheduleCalculator: Immediate mode returns null
- [ ] ScheduleCalculator: Delay mode with all units (2 seconds, 5 minutes, 3 hours, 7 days)
- [ ] ScheduleCalculator: Advance mode with all units
- [ ] ScheduleCalculator: ReferencePath extraction (valid DateTime property)
- [ ] ScheduleCalculator: ReferencePath fallback to 1970-01-01 (invalid path, wrong type, null value)
- [ ] ScheduleCalculator: Null ReferencePath uses provided referenceTime
- [ ] JSON parsing: All modes and units (case variations)
- [ ] JSON parsing: Defaults applied correctly
- [ ] JSON parsing: Invalid mode/unit throws meaningful error

**Integration Tests**:
- [ ] Rule with Immediate schedule executes successfully
- [ ] Rule with Delay schedule throws NotSupportedException
- [ ] Rule with Advance schedule throws NotSupportedException
- [ ] Exception message includes action type

**Future Considerations (Document but Don't Implement)**:
- [ ] Add note about `BlockPastEvents` flag (prevents execution if calculated time is past)
- [ ] Add note about strict vs non-strict reference path modes
- [ ] Add note about schema validation (where/how to validate Schedule metadata)

---

## Phase 3: CI/CD & NuGet Packaging

**User stories**: Infrastructure readiness for production use

### What to Build

Set up continuous integration, continuous deployment, and NuGet package publishing infrastructure. This enables automated testing, versioning, and distribution of RuleForge as a library.

**Key changes**:
- Create GitHub Actions workflow for CI (build + test)
- Create GitHub Actions workflow for NuGet publishing
- Configure versioning strategy (SemVer)
- Set up NuGet package metadata (authors, description, tags, license)
- Configure package icon and README
- Test package locally before publishing
- Publish to NuGet.org (or internal feed)

**End-to-end flow**: Push to main → CI builds and tests → create tag → publish to NuGet.

### Acceptance Criteria

**CI Workflow**:
- [ ] `.github/workflows/ci.yml` exists
- [ ] Workflow triggers on push/PR to main and develop branches
- [ ] Builds all projects in solution (`dotnet build`)
- [ ] Runs all tests (`dotnet test`)
- [ ] Runs on multiple OS (Ubuntu, Windows, macOS) or just Ubuntu
- [ ] Fails fast if build or tests fail

**NuGet Packaging**:
- [ ] `Directory.Build.props` or individual `.csproj` files have package metadata
- [ ] Package ID: `RuleForge.Abstractions`, `RuleForge.Core`, `RuleForge.Extensions.DependencyInjection`
- [ ] Authors, description, tags, license configured
- [ ] Package icon included (if applicable)
- [ ] README embedded in package
- [ ] Version follows SemVer (e.g., 0.1.0-preview)

**Publishing Workflow**:
- [ ] `.github/workflows/publish.yml` exists (or integrated in ci.yml)
- [ ] Triggers on tag push (e.g., `v0.1.0`)
- [ ] Builds and packs all projects (`dotnet pack`)
- [ ] Publishes to NuGet.org with API key (stored in secrets)
- [ ] Or publishes to GitHub Packages (alternative)

**Local Testing**:
- [ ] Can run `dotnet pack` locally and inspect `.nupkg` contents
- [ ] Can install local package in test project
- [ ] Package dependencies correct (Abstractions has no deps, Core depends on Abstractions)

**Documentation**:
- [ ] README.md updated with NuGet installation instructions
- [ ] Badge added to README showing build status and NuGet version
- [ ] ROADMAP.md updated to mark Phase 1.2 (Observability) and Phase 1.3 (Scheduling Model) complete

---

## Phase 4: Documentation & Samples

**User stories**: #13, #14 (from PRD), end-user documentation

### What to Build

Create comprehensive documentation and sample projects demonstrating observability and scheduling metadata usage. Update all existing documentation to reflect new capabilities. Optionally create a simple dummy scheduler to show how consumers would use scheduling metadata in practice.

**Key changes**:
- Create sample console app showing action results and scheduling metadata
- Update README.md with features overview
- Update QUICK_START.md with ActionResult handling and schedule metadata examples
- Create migration guide for breaking changes
- Update ROADMAP.md to mark completed phases
- **Optional**: Create simple in-memory scheduler demo (if feasible without complexity)

**End-to-end flow**: User follows QUICK_START.md → creates project → defines rules with observability → sees action results.

### Acceptance Criteria

**Sample Projects**:
- [ ] `samples/RuleForge.Samples.Console/` exists
- [ ] Console sample demonstrates current non-DI setup (`IEnumerable<IActionExecutor>`)
- [ ] Console sample shows how to handle `RuleExecutionResult` and `ActionResult`
- [ ] Console sample includes rule with schedule metadata (demonstrates JSON format)
- [ ] Console sample shows exception handling for `NotSupportedException` when schedule is used
- [ ] Sample includes JSON rule definitions in files
- [ ] Sample builds and runs successfully

**Dummy Scheduler (Optional)**:
- [ ] **If simple enough**: Create `samples/RuleForge.Samples.DummyScheduler/`
- [ ] Demonstrates how to process `ActionResult.Scheduled` items
- [ ] Shows in-memory queue or timer-based execution
- [ ] Clearly marked as "example only, not production-ready"
- [ ] **If too complex**: Skip and defer to Phase 5+

**Documentation**:
- [ ] README.md updated with "Features" section
- [ ] Features list: Observability (ActionResult), Scheduling Metadata (model only), Condition-based rules
- [ ] README.md includes installation instructions (NuGet package from Phase 3)
- [ ] README.md links to QUICK_START and samples
- [ ] QUICK_START.md updated with ActionResult handling examples
- [ ] QUICK_START.md includes Schedule metadata JSON examples
- [ ] QUICK_START.md explains current limitation (scheduler not implemented yet)
- [ ] Migration guide (`docs/MIGRATION.md`) documents all breaking changes from Phase 1
- [ ] Migration guide includes code examples: IActionExecutor return type, RuleEngine return type
- [ ] ROADMAP.md updated to mark Phase 1.2 (Observability) and Phase 1.3 (Scheduling Model) complete
- [ ] ROADMAP.md updated to defer original Phase 1.2 (UI) to later phase

**Testing**:
- [ ] All existing integration tests pass
- [ ] Sample projects verified to build and run
- [ ] Documentation reviewed for accuracy

---

## Notes

### Dependencies Between Phases

- **Phase 1** (Observability) is independent, can be done first
- **Phase 2** (Scheduling Model) depends on Phase 1 (needs ActionResult with ScheduledFor property)
- **Phase 3** (CI/CD) can be done anytime after Phase 2 (needs working code to package)
- **Phase 4** (Documentation) depends on Phase 3 (needs NuGet package to document installation)
- **Phase 5+** (DI, Scheduler Implementation) depend on all previous phases

### Testing Strategy

Each phase includes its own tests:
- **Phase 1**: ActionResult unit tests, RuleExecutionResult aggregation, integration tests for result collection
- **Phase 2**: ScheduleCalculator unit tests (all modes/units/paths), JSON parsing tests, RuleEngine exception tests
- **Phase 3**: CI workflow validation, NuGet package testing
- **Phase 4**: Sample project verification, documentation review

### Future Extensions (Out of Scope)

**Phase 5+: Dependency Injection Support**
- `IActionExecutorFactory` abstraction
- `DirectActionExecutorFactory` for non-DI scenarios
- `ServiceProviderActionExecutorFactory` for DI scenarios
- `RuleForge.Extensions.DependencyInjection` project
- Fluent registration API (`AddRuleForge()`, `AddAction<T>()`)

**Phase 6+: Scheduler Implementation**
- Actual execution of scheduled actions
- Hangfire integration
- Quartz.NET integration
- Native background service implementation
- `IScheduler` abstraction

**Phase 7+: Advanced Features**
- Event-based observability (IActionExecutionObserver)
- Timezone support in ScheduleMetadata
- Cron expressions for recurring schedules
- `BlockPastEvents` flag (prevent execution if scheduled time is past)
- Strict vs non-strict reference path modes
- Schema validation for Schedule metadata
- UI/management features

### Design Decisions for Future Consideration

1. **Schema Validation**: Where and how to validate `ScheduleMetadata` (parser vs calculator vs both)
2. **BlockPastEvents Flag**: Add to `ScheduleMetadata` to prevent execution if calculated time is in the past
3. **Strict Reference Paths**: Option to throw vs fall back when reference path is invalid
4. **Time Provider Abstraction**: If testing requires mocking `DateTime.UtcNow`, introduce `ITimeProvider`
5. **Negative/Zero Amounts**: Should these be allowed or validated away?

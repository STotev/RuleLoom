# Design: Action Factory Pattern & RuleEngine Update (Phase 1.2B)

**Issue:** [#3 — Phase 1.2B: Action Factory Pattern & RuleEngine Update](https://github.com/STotev/RuleForge/issues/3)  
**Parent PRD:** [#1 — PRD: Action Scheduling & Dependency Injection Support](https://github.com/STotev/RuleForge/issues/1)  
**Depends on:** Phase 1.2A (issue #2, closed) — `ActionResult`, `RuleExecutionResult`, `ScheduleMetadata`, and `IActionExecutor<ActionResult>` are already in place.

---

## Problem

`RuleEngine` currently accepts `IEnumerable<IActionExecutor>` directly and builds its own action dictionary internally. This tight coupling prevents:

- Mocking action resolution in tests without providing concrete instances
- DI-based resolution of scoped action executors (Phase 1.2C)
- Capturing and returning action execution outcomes

`ExecuteAsync` returns `Task` (void), so callers have no visibility into what ran, what was scheduled, or what failed.

---

## Solution

Introduce `IActionExecutorFactory` as the resolution abstraction and `DirectActionExecutorFactory` as the default non-DI implementation. Update `RuleEngine` to use the factory and return `RuleExecutionResult` from `ExecuteAsync`.

---

## Architecture

### New Types in `RuleForge.Abstractions`

#### `IActionExecutorFactory` (`Action/IActionExecutorFactory.cs`)

```csharp
namespace RuleForge.Abstractions.Action;

public interface IActionExecutorFactory
{
    bool TryGetExecutor(string type, out IActionExecutor? executor);
}
```

- `TryGetExecutor` mirrors the standard .NET Try-pattern
- Returns `false` for unknown types; engine decides how to handle the gap
- Zero new dependencies — lives cleanly in Abstractions

#### `RuleActionResult` (`Action/RuleActionResult.cs`)

```csharp
namespace RuleForge.Abstractions.Action;

public sealed record RuleActionResult(ActionResult Result, string RuleName, string ActionType);
```

- Wraps an `ActionResult` with the context of which rule and action type produced it
- Kept separate from `ActionResult` so executor implementations remain unaware of rule context
- `RuleName` and `ActionType` enable diagnostics without exposing the full `ActionDefinition`

#### Updated `RuleExecutionResult` (`Rule/RuleExecutionResult.cs`)

```csharp
public sealed record RuleExecutionResult
{
    // Only property — IReadOnlyList<ActionResult> changed to IReadOnlyList<RuleActionResult>
    public IReadOnlyList<RuleActionResult> ActionResults { get; init; } = [];
}
```

Change: `IReadOnlyList<ActionResult>` → `IReadOnlyList<RuleActionResult>`. No other properties are removed or renamed.

---

### New Type in `RuleForge.Core`

#### `DirectActionExecutorFactory` (`Actions/DirectActionExecutorFactory.cs`)

```csharp
namespace RuleForge.Actions;

public sealed class DirectActionExecutorFactory : IActionExecutorFactory
{
    private readonly Dictionary<string, IActionExecutor> _executors;

    public DirectActionExecutorFactory(IEnumerable<IActionExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors.ToDictionary(x => x.Type);
    }

    public bool TryGetExecutor(string type, out IActionExecutor? executor)
        => _executors.TryGetValue(type, out executor);
}
```

- Preserves the exact lookup behavior that `RuleEngine` previously owned
- Dictionary is populated once at construction and never mutated; plain `Dictionary<K,V>` is safe for concurrent reads
- Migration path: `new DirectActionExecutorFactory(actions)` replaces passing the raw list

---

### Updated `RuleEngine`

#### Constructor

```csharp
public RuleEngine(IConditionEvaluator conditionEvaluator, IActionExecutorFactory actionFactory)
```

#### `ExecuteAsync` — Outcome Paths

All four action outcomes produce a `RuleActionResult` and are added to the aggregate result:

| Scenario | `ActionResult` produced |
|---|---|
| Action executed successfully | `ActionResult` returned by executor |
| Action condition evaluates to false | `ActionResult.Skipped("Condition not met.")` |
| Action type not registered in factory | `ActionResult.Failed(...)` with descriptive message |
| Executor throws an exception | `ActionResult.Failed(exception)` |

- Each path **continues to the next action** — no exception propagates out of the action loop
- `OperationCanceledException` is re-thrown immediately (cancellation is not swallowed)
- The outer per-rule try-catch (for condition evaluation errors) is unchanged

#### Both Overloads Return `Task<RuleExecutionResult>`

```csharp
// Primary overload
public async Task<RuleExecutionResult> ExecuteAsync(
    string eventType, object current, object? previous,
    IEnumerable<RuleDefinition> rules, CancellationToken cancellationToken = default)

// Convenience overload (no previous state)
public Task<RuleExecutionResult> ExecuteAsync(
    string eventType, object data,
    IEnumerable<RuleDefinition> rules, CancellationToken cancellationToken = default)
```

---

## Data Flow

```
ExecuteAsync(eventType, current, previous, rules)
  │
  ├─ Filter rules by eventType, sort by Priority (descending)
  │
  └─ foreach rule:
       ├─ [try] Evaluate rule condition
       │    └─ false → skip rule (no result entry)
       │
       └─ foreach action in rule.Actions:
            ├─ Evaluate action.Condition (if present)
            │    └─ false → RuleActionResult(Skipped, ruleName, actionType)
            │
            ├─ factory.TryGetExecutor(action.Type)
            │    └─ false → RuleActionResult(Failed("not registered"), ruleName, actionType)
            │
            └─ [try] executor.ExecuteAsync(action, context)
                 ├─ success → RuleActionResult(result, ruleName, actionType)
                 └─ throws → RuleActionResult(Failed(ex), ruleName, actionType)
  │
  └─ return new RuleExecutionResult { ActionResults = collected list }
```

---

## API Usage

### Non-DI Setup (updated)

```csharp
var actions = new IActionExecutor[] { new LogAction(), new SendEmailAction() };
var factory = new DirectActionExecutorFactory(actions);
var engine = new RuleEngine(conditionEvaluator, factory);

var result = await engine.ExecuteAsync(eventType, current, previous, rules);

foreach (var r in result.ActionResults)
{
    Console.WriteLine($"[{r.RuleName}] {r.ActionType}: {r.Result.Status}");
}
```

### Migration from Phase 1.2A

```csharp
// Before
new RuleEngine(conditionEvaluator, actions)

// After
new RuleEngine(conditionEvaluator, new DirectActionExecutorFactory(actions))
```

---

## Testing Plan

### Unit Tests (`RuleForge.Tests`)

**`DirectActionExecutorFactoryTests`**
- `TryGetExecutor_RegisteredType_ReturnsTrueAndExecutor`
- `TryGetExecutor_UnknownType_ReturnsFalse`
- `Constructor_NullExecutors_ThrowsArgumentNullException`
- `Constructor_DuplicateType_ThrowsArgumentException` (from `ToDictionary` behavior)

**`RuleEngineTests`** (new, unit — uses mock/stub factory)
- `ExecuteAsync_ActionExecutesSuccessfully_ReturnsSuccessResult`
- `ExecuteAsync_UnknownActionType_ReturnsFailedResult`
- `ExecuteAsync_UnknownActionType_ContinuesToNextAction`
- `ExecuteAsync_ExecutorThrows_ReturnsFailedResult`
- `ExecuteAsync_ExecutorThrows_ContinuesToNextAction`
- `ExecuteAsync_ActionConditionFalse_ReturnsSkippedResult`
- `ExecuteAsync_MultipleRules_AggregatesAllResults`
- `ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException`

### Integration Tests (`RuleForge.Tests.Integration`)

- Update `RuleEngineIntegrationTests` constructor: wrap `actions` list in `DirectActionExecutorFactory`
- Add result assertions to existing tests. At minimum:

```csharp
var result = await _engine.ExecuteAsync("PropertyUpdated", property, [rule]);

Assert.Single(result.ActionResults);
Assert.Equal(ActionExecutionStatus.ExecutedImmediately, result.ActionResults[0].Result.Status);
Assert.Equal("Price Equals Test", result.ActionResults[0].RuleName);
Assert.Equal("test-log", result.ActionResults[0].ActionType);
```

### Console Sample (`samples/RuleForge.Samples.Console/Program.cs`)

- Wrap `actions` in `new DirectActionExecutorFactory(actions)` before passing to `RuleEngine`

---

## Out of Scope

- `ServiceProviderActionExecutorFactory` — Phase 1.2C (DI Extensions)
- Event-based observability (`IActionExecutionObserver`) — Phase 2.x
- Timezone support — Phase 3.x
- Dynamic executor registration after factory construction

---

## Constraints

- `IActionExecutorFactory` must have zero new dependencies (Abstractions constraint)
- `DirectActionExecutorFactory` may only depend on `RuleForge.Abstractions`
- `dotnet build` and `dotnet test` must pass with no regressions

---

## Deviations

Recorded retrospectively (2026-10-02) by comparing this spec with the code. Reasons were not
recorded at the time.

- **Results are grouped per rule instead of flattened.** Both `ExecuteAsync` overloads return
  `Task<IReadOnlyList<RuleExecutionResult>>`, one entry per rule whose condition matched, each with
  `RuleName` and `IReadOnlyList<ActionResult> ActionResults`. `RuleActionResult` was not introduced,
  so a result does not carry its action's `Type` — callers correlate by index with `rule.Actions`.
- **Rules whose condition is false produce no result entry**, and a rule that throws is written to
  `Console.Error` and also produces no entry.
- **`ExecuteAsync` takes a `CancellationToken`**; `OperationCanceledException` always propagates.

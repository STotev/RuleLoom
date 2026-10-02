# Design: Phase 1.2A — Scheduling Model & Abstractions

**Issue:** [#2 Phase 1.2A: Scheduling Model & Abstractions](https://github.com/STotev/RuleForge/issues/2)  
**Parent PRD:** [#1 PRD: Action Scheduling & Dependency Injection Support](https://github.com/STotev/RuleForge/issues/1)  
**Date:** 2026-03-28  
**Status:** Approved

---

## Problem

`IActionExecutor.ExecuteAsync` returns `Task` (void), providing no signal to callers about whether an action executed immediately, was scheduled for future execution, was skipped, or failed. There is also no model for carrying scheduling metadata in the JSON rule definition, and no structured result type for aggregating outcomes across a rule's actions.

This phase adds the foundational data model and updates the action contract. No scheduler implementation is included — that is Phase 1.2D.

---

## Approach

**Option A — Minimal breaking change (selected)**

Add all new types to `RuleForge.Abstractions`, update `IActionExecutor.ExecuteAsync` to return `Task<ActionResult>`, extend `ActionDefinition` with an optional `Schedule` property, update `RuleJsonParser` to deserialize the schedule block, and update `LogAction` to return `ActionResult.Success()`.

`RuleEngine` is **not changed** in this phase. It continues to return `Task` (void). The engine will be updated in Phase 1.2B when `IActionExecutorFactory` is introduced, enabling collection of `ActionResult`s into a `RuleExecutionResult`.

---

## Namespace Layout

| Type | Namespace | File |
|---|---|---|
| `ScheduleMode` | `RuleForge.Abstractions.Scheduling` | `Scheduling/ScheduleMode.cs` |
| `ScheduleUnit` | `RuleForge.Abstractions.Scheduling` | `Scheduling/ScheduleUnit.cs` |
| `ScheduleMetadata` | `RuleForge.Abstractions.Scheduling` | `Scheduling/ScheduleMetadata.cs` |
| `ActionExecutionStatus` | `RuleForge.Abstractions.Action` | `Action/ActionExecutionStatus.cs` |
| `ActionResult` | `RuleForge.Abstractions.Action` | `Action/ActionResult.cs` |
| `RuleExecutionResult` | `RuleForge.Abstractions.Rule` | `Rule/RuleExecutionResult.cs` |

The `Scheduling` sub-namespace mirrors the existing `Condition` pattern: a cohesive group of related types lives together under a named sub-namespace.

`ActionResult` and `ActionExecutionStatus` are action-scoped results and belong in `RuleForge.Abstractions.Action` alongside `ActionDefinition` and `IActionExecutor`.

`RuleExecutionResult` wraps the outcome of an entire rule execution and belongs in `RuleForge.Abstractions.Rule` alongside `RuleDefinition`.

---

## New Types

### `ScheduleMode` (enum)

```csharp
namespace RuleForge.Abstractions.Scheduling;

public enum ScheduleMode
{
    Immediate,
    Delay,
    Advance
}
```

### `ScheduleUnit` (enum)

```csharp
namespace RuleForge.Abstractions.Scheduling;

public enum ScheduleUnit
{
    Seconds,
    Minutes,
    Hours,
    Days
}
```

### `ScheduleMetadata` (sealed record)

```csharp
namespace RuleForge.Abstractions.Scheduling;

public sealed record ScheduleMetadata
{
    public ScheduleMode Mode { get; init; } = ScheduleMode.Immediate;

    /// <summary>Null when Mode is Immediate.</summary>
    public int? Amount { get; init; }

    /// <summary>Null when Mode is Immediate.</summary>
    public ScheduleUnit? Unit { get; init; }

    /// <summary>
    /// Property path into the domain object to use as the reference DateTime.
    /// When null, the event reception time (DateTime.UtcNow) is used as the reference.
    /// Resolved by PathResolver in Phase 1.2D.
    /// </summary>
    public string? ReferencePath { get; init; }
}
```

`Amount` and `Unit` are nullable to make it structurally clear that they have no meaning in `Immediate` mode.

### `ActionExecutionStatus` (enum)

```csharp
namespace RuleForge.Abstractions.Action;

public enum ActionExecutionStatus
{
    ExecutedImmediately,
    Scheduled,
    Skipped,
    Failed
}
```

### `ActionResult` (sealed record)

```csharp
namespace RuleForge.Abstractions.Action;

public sealed record ActionResult
{
    public ActionExecutionStatus Status { get; init; }
    public DateTime? ScheduledFor { get; init; }
    public string? Message { get; init; }
    public Exception? Error { get; init; }

    public static ActionResult Success()
        => new() { Status = ActionExecutionStatus.ExecutedImmediately };

    public static ActionResult Scheduled(DateTime scheduledFor)
        => new() { Status = ActionExecutionStatus.Scheduled, ScheduledFor = scheduledFor };

    public static ActionResult Skipped(string? reason = null)
        => new() { Status = ActionExecutionStatus.Skipped, Message = reason };

    public static ActionResult Failed(Exception error)
        => new() { Status = ActionExecutionStatus.Failed, Error = error };
}
```

Static factory methods are the only intended construction paths. The parameterless `init`-only constructor is a technical necessity for the record type.

### `RuleExecutionResult` (sealed record)

```csharp
namespace RuleForge.Abstractions.Rule;

public sealed record RuleExecutionResult(IReadOnlyList<ActionResult> ActionResults);
```

A thin wrapper. Provides a named return type for `RuleEngine.ExecuteAsync` when it is updated in Phase 1.2B. Kept minimal intentionally — do not add rule metadata until there is a concrete use case.

---

## Contract Changes

### `ActionDefinition` — add `Schedule` property

```csharp
// In RuleForge.Abstractions.Action.ActionDefinition
using RuleForge.Abstractions.Scheduling;

/// <summary>
/// Optional scheduling metadata. When null, the action executes immediately (default).
/// </summary>
public ScheduleMetadata? Schedule { get; init; }
```

No change to existing JSON serialization for rules without a `schedule` block — `Schedule` is null by default and `DefaultIgnoreCondition = WhenWritingNull` already handles omission.

### `IActionExecutor` — breaking signature change

```csharp
// Old:
Task ExecuteAsync(ActionDefinition action, EvaluationContext context);

// New:
Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context);
```

Migration: return `ActionResult.Success()` from existing implementations.

---

## JSON Deserialization

`ScheduleMetadata` is a plain object with enum string properties. It fits naturally into the existing `System.Text.Json` pipeline.

**Required change to `RuleJsonParser`:** add `JsonStringEnumConverter` to the `JsonSerializerOptions` to support string-form enum values (e.g., `"mode": "Delay"`). The existing `PropertyNameCaseInsensitive = true` handles case-insensitive property names already.

```csharp
// In RuleJsonParser.CreateDefaultOptions():
Converters =
{
    new ConditionNodeJsonConverter(),
    new JsonStringEnumConverter() // NEW — no naming policy; maps to PascalCase member names, case-insensitive on read
}
```

**Supported JSON forms (all three must round-trip):**

```json
// Immediate (no schedule block — default)
{ "type": "send-email", "parameters": { "to": "{{current.Email}}" } }

// Delay from event time
{
  "type": "send-reminder",
  "schedule": { "mode": "Delay", "amount": 24, "unit": "Hours" }
}

// Advance before domain date
{
  "type": "send-warning",
  "schedule": { "mode": "Advance", "amount": 2, "unit": "Hours", "referencePath": "Order.DueDate" }
}
```

---

## `LogAction` Update

```csharp
public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context)
{
    if (action.Parameters.TryGetValue("message", out var message))
        Console.WriteLine(message);
    return Task.FromResult(ActionResult.Success());
}
```

---

## Tests

### New: `ActionResultTests.cs` (in `RuleForge.Tests`)

| Test | Scenario |
|---|---|
| `Success_ReturnsExecutedImmediately` | Status = ExecutedImmediately, all other fields null |
| `Scheduled_SetsScheduledFor` | Status = Scheduled, ScheduledFor = provided DateTime |
| `Failed_SetsError` | Status = Failed, Error = provided exception |
| `Skipped_ReturnsSkipped` | Status = Skipped |
| `Skipped_WithReason_SetsMessage` | Status = Skipped, Message = provided reason |

### Updated: `RuleJsonParserTests.cs` (in `RuleForge.Tests`)

| Test | Scenario |
|---|---|
| `Parse_ActionWithNoSchedule_ScheduleIsNull` | Omitted schedule block: `Schedule` is null (default immediate) |
| `Parse_ActionWithImmediateSchedule_Success` | Explicit `{ "mode": "Immediate" }` block: `Mode = Immediate`, `Amount`/`Unit` null |
| `Parse_ActionWithDelaySchedule_Success` | `mode: Delay`, `amount: 24`, `unit: Hours` round-trip |
| `Parse_ActionWithAdvanceSchedule_Success` | `mode: Advance`, with `referencePath` round-trip |
| `Parse_ScheduleEnumCaseInsensitive_Success` | `"mode": "delay"` deserializes to `ScheduleMode.Delay` |

### Updated: `RuleEngineIntegrationTests.cs` (in `RuleForge.Tests.Integration`)

`TestLogAction` must be updated to return `Task<ActionResult>` (return `Task.FromResult(ActionResult.Success())`). All existing test assertions remain valid — no behavioral change.

---

## What Is Explicitly Out of Scope

- `IActionExecutorFactory` and `DirectActionExecutorFactory` → Phase 1.2B
- `RuleEngine` returning `RuleExecutionResult` → Phase 1.2B
- `ScheduleCalculator` date arithmetic → Phase 1.2D
- Actual scheduler dispatch (Hangfire, Quartz, background services) → Phase 2.x
- Timezone support → Phase 3.x

---

## Breaking Change Migration Guide

```csharp
// Old IActionExecutor implementation:
public Task ExecuteAsync(ActionDefinition action, EvaluationContext context)
{
    // ...do work...
    return Task.CompletedTask;
}

// New IActionExecutor implementation:
public async Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context)
{
    // ...do work...
    return ActionResult.Success();
}
```

If the action uses `Task.CompletedTask` (sync work), switch to:
```csharp
public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context)
{
    // ...do work...
    return Task.FromResult(ActionResult.Success());
}
```

---

## Deviations

Recorded retrospectively (2026-10-02) by comparing this spec with the code. Reasons were not
recorded at the time unless stated.

- **`ActionResult` factories take an optional `message`**: `Success(string? message = null)`,
  `Scheduled(DateTimeOffset, string? message = null)`, `Failed(Exception, string? message = null)`.
  `Success()` with no message returns a cached instance. `Failed` throws on a null `error`.
- **`ActionResult.ScheduledFor` is `DateTimeOffset?`**, not `DateTime?`, and `Scheduled()` normalises
  to UTC. Changed in Phase 1.2D — see `2026-03-29-schedule-calculation-engine-design.md`.
- **`IActionExecutor.ExecuteAsync` takes a `CancellationToken`** (default `default`), added in
  commit `f2ec235` and propagated through `RuleEngine`.
- **`RuleExecutionResult` is a record with `RuleName` and `ActionResults` init properties**, not a
  positional record with `ActionResults` only. Changed in Phase 1.2B — see
  `2026-03-28-action-factory-pattern-design.md`.

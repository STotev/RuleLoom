# Phase 1.2D: Schedule Calculation Engine — Design Spec

**Issue**: #4  
**Blocked by**: #2 (done), #3 (done)  
**Date**: 2026-03-29

---

## Problem

`ScheduleMetadata` is parsed and stored on `ActionDefinition.Schedule`, but `RuleEngine` ignores it — every action executes immediately regardless. This issue wires the schedule metadata into the execution path so that non-immediate actions return `ActionResult.Scheduled(scheduledFor)` instead of calling `ExecuteAsync`.

---

## Decisions Made

| Question | Decision | Rationale |
|---|---|---|
| `ScheduleCalculator` design | Internal static class, pure function | No external consumers; calculation is deterministic once you have the inputs |
| Clock abstraction | `TimeProvider` (optional) on `RuleEngine`; passes `DateTimeOffset utcNow` to `Calculate` | `TimeProvider` is the official .NET 8+ clock abstraction; `ScheduleCalculator` stays dependency-free |
| Interface (`IScheduleCalculator`) | **Not introduced** | Interface solves the wrong problem — `TimeProvider` already handles testability of the clock |
| `ActionResult.ScheduledFor` type | Upgrade `DateTime?` → `DateTimeOffset?` | Unambiguous timezone semantics; `TimeProvider.GetUtcNow()` returns `DateTimeOffset` |
| Missing `ReferencePath` | Throw `InvalidOperationException` | Being strict is better for a library; explicit failure surfaces misconfiguration early |
| Non-DateTime `ReferencePath` | Throw `InvalidOperationException` | Same rationale — misconfigured rule should fail visibly |
| Past-date result | Return `Scheduled` as-is | RuleForge defers scheduler dispatch to the consumer; computing a past date is valid output |

---

## Architecture

### New File

```
src/RuleForge/Scheduling/ScheduleCalculator.cs
```

### Changed Files

| File | Change |
|---|---|
| `src/RuleForge.Abstractions/Action/ActionResult.cs` | `ScheduledFor`: `DateTime?` → `DateTimeOffset?`; `Scheduled()` param: `DateTime` → `DateTimeOffset` |
| `src/RuleForge/Engine/RuleEngine.cs` | Optional `TimeProvider? timeProvider = null` constructor param; scheduling check in action loop |

---

## Component Design

### `ScheduleCalculator` (new)

```csharp
// src/RuleForge/Scheduling/ScheduleCalculator.cs
namespace RuleForge.Scheduling;

internal static class ScheduleCalculator
{
    /// <summary>
    /// Computes the scheduled execution time for an action.
    /// </summary>
    /// <param name="schedule">The scheduling metadata. Mode must be Delay or Advance.</param>
    /// <param name="context">Evaluation context used to resolve ReferencePath.</param>
    /// <param name="utcNow">The current UTC time; used when ReferencePath is null.</param>
    /// <returns>The UTC DateTimeOffset at which the action should execute.</returns>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when ReferencePath is set but does not resolve to a DateTime/DateTimeOffset,
    ///   or when Amount is null for a Delay/Advance schedule.
    /// </exception>
    public static DateTimeOffset Calculate(
        ScheduleMetadata schedule,
        EvaluationContext context,
        DateTimeOffset utcNow)
    {
        var referenceTime = ResolveReferenceTime(schedule, context, utcNow);
        var offset = ComputeOffset(schedule);

        return schedule.Mode switch
        {
            ScheduleMode.Delay   => referenceTime + offset,
            ScheduleMode.Advance => referenceTime - offset,
            _ => throw new ArgumentException(
                     $"Cannot calculate schedule for mode '{schedule.Mode}'.", nameof(schedule))
        };
    }

    private static DateTimeOffset ResolveReferenceTime(
        ScheduleMetadata schedule, EvaluationContext context, DateTimeOffset utcNow)
    {
        if (schedule.ReferencePath is null)
            return utcNow;

        var resolved = context.Resolve(schedule.ReferencePath);

        return resolved switch
        {
            DateTimeOffset dto => dto,
            DateTime dt        => new DateTimeOffset(dt, TimeSpan.Zero),
            null => throw new InvalidOperationException(
                       $"ReferencePath '{schedule.ReferencePath}' resolved to null. " +
                       $"Ensure the property exists on the domain object."),
            _ => throw new InvalidOperationException(
                       $"ReferencePath '{schedule.ReferencePath}' resolved to '{resolved.GetType().Name}' " +
                       $"but a DateTime or DateTimeOffset value is required.")
        };
    }

    private static TimeSpan ComputeOffset(ScheduleMetadata schedule)
    {
        var amount = schedule.Amount ?? throw new InvalidOperationException(
            $"Schedule.Amount is required for '{schedule.Mode}' mode.");

        return schedule.Unit switch
        {
            ScheduleUnit.Seconds => TimeSpan.FromSeconds(amount),
            ScheduleUnit.Minutes => TimeSpan.FromMinutes(amount),
            ScheduleUnit.Hours   => TimeSpan.FromHours(amount),
            ScheduleUnit.Days    => TimeSpan.FromDays(amount),
            _ => throw new ArgumentException(
                     $"Unknown schedule unit '{schedule.Unit}'.", nameof(schedule))
        };
    }
}
```

### `RuleEngine` changes

**Constructor** — add optional `TimeProvider`:

```csharp
public RuleEngine(
    IConditionEvaluator conditionEvaluator,
    IActionExecutorFactory actionFactory,
    TimeProvider? timeProvider = null)
{
    _conditionEvaluator = conditionEvaluator ?? throw new ArgumentNullException(nameof(conditionEvaluator));
    _actionFactory      = actionFactory      ?? throw new ArgumentNullException(nameof(actionFactory));
    _timeProvider       = timeProvider       ?? TimeProvider.System;
}
```

**Action loop** — insert scheduling check after action condition evaluation:

```csharp
// After action condition check, before factory lookup
if (action.Schedule is { Mode: not ScheduleMode.Immediate })
{
    try
    {
        var scheduledFor = ScheduleCalculator.Calculate(
            action.Schedule, context, _timeProvider.GetUtcNow());
        actionResults.Add(ActionResult.Scheduled(
            scheduledFor, $"Action '{action.Type}' scheduled for {scheduledFor:O}."));
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        actionResults.Add(ActionResult.Failed(
            ex, $"Action '{action.Type}' scheduling failed: {ex.Message}"));
    }
    continue;
}
```

### `ActionResult` changes

```csharp
// ScheduledFor: DateTime? → DateTimeOffset?
public DateTimeOffset? ScheduledFor { get; init; }

// Scheduled() factory method parameter: DateTime → DateTimeOffset
public static ActionResult Scheduled(DateTimeOffset scheduledFor, string? message = null)
```

---

## Execution Flow

```
RuleEngine.ExecuteAsync(eventType, current, previous, rules)
  → filter rules by eventType, sort by Priority (desc)
  → for each rule:
      → evaluate rule condition → false: skip rule
      → for each action:
          → action.Condition != null → evaluate → false: ActionResult.Skipped, continue
          → action.Schedule != null && Mode != Immediate:
              → ScheduleCalculator.Calculate(schedule, context, _timeProvider.GetUtcNow())
                  → resolve reference time (ReferencePath or utcNow)
                  → compute offset from Amount × Unit
                  → apply Delay (+) or Advance (-)
              → success: ActionResult.Scheduled(scheduledFor)
              → exception: ActionResult.Failed(ex)
              → continue (ExecuteAsync NOT called)
          → otherwise: factory.TryGetExecutor → execute → ActionResult
```

---

## Test Plan

### `ScheduleCalculatorTests` (unit, `RuleForge.Tests`)

**Mode × Unit matrix** (8 tests, using fixed `utcNow`):

| Test | Mode | Unit | Amount | Expected |
|---|---|---|---|---|
| `Calculate_DelaySeconds` | Delay | Seconds | 30 | utcNow + 30s |
| `Calculate_DelayMinutes` | Delay | Minutes | 2 | utcNow + 2min |
| `Calculate_DelayHours` | Delay | Hours | 24 | utcNow + 24h |
| `Calculate_DelayDays` | Delay | Days | 7 | utcNow + 7d |
| `Calculate_AdvanceSeconds` | Advance | Seconds | 30 | ref - 30s |
| `Calculate_AdvanceMinutes` | Advance | Minutes | 2 | ref - 2min |
| `Calculate_AdvanceHours` | Advance | Hours | 2 | ref - 2h |
| `Calculate_AdvanceDays` | Advance | Days | 1 | ref - 1d |

**ReferencePath resolution** (4 tests):

| Test | Setup | Expected |
|---|---|---|
| `Calculate_ReferencePathNull_UsesUtcNow` | No path, utcNow = T | T + offset |
| `Calculate_ReferencePathResolvesDateTime` | Path → `DateTime` property | (DateTime as UTC) + offset |
| `Calculate_ReferencePathResolvesDateTimeOffset` | Path → `DateTimeOffset` property | dto + offset |
| `Calculate_AdvanceBeforeDueDate_ResultMayBePast` | Advance 5h, ref = utcNow + 2h | utcNow - 3h (past, valid) |

**Error cases** (4 tests):

| Test | Setup | Expected |
|---|---|---|
| `Calculate_ReferencePathMissing_Throws` | Path → non-existent property | `InvalidOperationException` |
| `Calculate_ReferencePathWrongType_Throws` | Path → `string` property | `InvalidOperationException` |
| `Calculate_AmountNull_Throws` | `Amount = null` on Delay | `InvalidOperationException` |
| `Calculate_ImmediateMode_Throws` | Mode = Immediate | `ArgumentException` |

### `RuleEngineTests` additions (unit, `RuleForge.Tests`)

| Test | Scenario | Expected |
|---|---|---|
| `ExecuteAsync_DelaySchedule_ReturnsScheduledResult` | Action has Delay 1h schedule, no ReferencePath | `ActionResult.Scheduled`, `ScheduledFor` = fakeNow + 1h |
| `ExecuteAsync_AdvanceScheduleWithReferencePath_ReturnsScheduledResult` | Action has Advance 2h, path resolves to a DateTimeOffset | `ActionResult.Scheduled`, correct `ScheduledFor` |
| `ExecuteAsync_NullSchedule_ExecutesImmediately` | Action has no schedule | `ActionResult.ExecutedImmediately` (regression) |
| `ExecuteAsync_ImmediateModeSchedule_ExecutesImmediately` | Action has `Mode = Immediate` | `ActionResult.ExecutedImmediately` |
| `ExecuteAsync_ActionConditionFalseWithSchedule_ReturnsSkipped` | Action condition false, schedule present | `ActionResult.Skipped` (condition checked first) |
| `ExecuteAsync_InvalidReferencePath_ReturnsFailedResult` | ReferencePath → wrong type | `ActionResult.Failed` |

---

## Out of Scope (this issue)

- Timezone support (deferred to Phase 3.x)
- Actual scheduler dispatch (Hangfire, Quartz — Phase 2.x)
- DI wiring of `TimeProvider` into `RuleEngine` (that's Phase 1.2C/E work)

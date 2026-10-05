# Design: Complete Engine Results

**Issue:** [#32 API hardening: complete engine results](https://github.com/STotev/RuleForge/issues/32) (also closes #18)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleForge/issues/22) (module 8)
**Date:** 2026-10-05
**Status:** Draft — awaiting maintainer review (HITL)

---

## Problem

`RuleEngine.ExecuteAsync` returns `IReadOnlyList<RuleExecutionResult>`, which leaves callers unable to tell what
happened:

- Rules whose condition is false produce **no entry**, so "didn't match" and "wasn't evaluated" look the same.
- A rule that throws (e.g. an unknown operator in its condition) produces **no entry**; the error only goes to
  `Console.Error`, which a library shouldn't write to.
- If an action condition throws after earlier actions already ran, the **whole rule's results are discarded**:
  their side effects happened, but the caller is never told.
- A `RuleExecutionResult` has only `RuleName` (not unique), and an `ActionResult` doesn't say which action it
  belongs to, so callers must correlate by position.
- The cancellation token is only passed to executors; the engine never checks it between rules or actions (#18).

## Proposed shape

```csharp
namespace RuleForge.Abstractions.Rule;

public sealed record RuleEngineResult
{
    // One entry per rule whose EventType matched, in execution order (priority, descending).
    public IReadOnlyList<RuleExecutionResult> Rules { get; init; } = [];

    // True if any rule or any action has status Failed.
    public bool HasFailures { get; }

    // Every Scheduled action across all rules, with its rule identity (see Q1).
    public IReadOnlyList<ScheduledAction> ScheduledActions { get; }
}

public sealed record RuleExecutionResult
{
    public required string RuleId { get; init; }
    public required string RuleName { get; init; }
    public RuleExecutionStatus Status { get; init; }      // Matched / NotMatched / Failed
    public Exception? Error { get; init; }                // set when Status == Failed
    public IReadOnlyList<ActionResult> ActionResults { get; init; } = [];
}

public enum RuleExecutionStatus { Matched, NotMatched, Failed }

public sealed record ScheduledAction(string RuleId, string RuleName, ActionDefinition Action, ActionResult Result);
```

`ActionResult` (namespace `RuleForge.Abstractions.Action`) gains two properties, which the **engine** sets on every
result, including the ones returned by executors (see Q3):

```csharp
public string ActionType { get; init; } = string.Empty;  // the action definition's Type
public int ActionIndex { get; init; }                     // position in RuleDefinition.Actions
```

The existing `ActionResult` factory methods (`Success`, `Scheduled`, `Skipped`, `Failed`) are unchanged.

## Behaviour

```
ExecuteAsync(eventType, current, previous, rules) → RuleEngineResult
  rules with another EventType          → not included
  for each rule (priority descending):
    cancellation requested              → throw OperationCanceledException
    rule condition throws               → Status = Failed, Error = exception, no actions run
    rule condition false                → Status = NotMatched, ActionResults = []
    rule condition true                 → Status = Matched, then for each action:
      cancellation requested            → throw OperationCanceledException
      action condition throws           → that action Failed; continue with the next action
      action condition false            → Skipped
      Schedule with Mode != Immediate   → Scheduled (or Failed if the calculation throws)
      no executor for Type              → Failed
      executor throws / returns null    → Failed
      otherwise                         → the executor's ActionResult
    every ActionResult is stamped with ActionType and ActionIndex
  OperationCanceledException always propagates
```

A rule whose actions failed keeps `Status = Matched` (the rule did match); the failures are visible in its
`ActionResults` and in `HasFailures` (see Q2). Nothing is written to the console.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Where the types live | `RuleForge.Abstractions.Rule` (`RuleEngineResult`, `RuleExecutionStatus`, `ScheduledAction`) | Results are contracts that DI (#5) and future scheduler integrations consume; Abstractions stays dependency-free |
| Rules with another event type | Not included | They weren't considered for this event (PRD) |
| `NotMatched` entries | Included, with empty `ActionResults` | Callers can tell "didn't match" from "wasn't considered" |
| Rule ID | `RuleId` added; `RuleName` kept | Names aren't unique; IDs are generated when absent |
| Errors | The exception object on `Error` (rule) and `ActionResult.Error` (action) | Consistent with the existing `ActionResult.Failed(Exception)` |
| Console output | Removed | Library code shouldn't write to the console; the results now carry every error. Structured logging stays in the observability backlog |
| Cancellation | Checked before each rule and before each action (#18) | |
| Wrapper type | `RuleEngineResult` instead of a bare list | PRD decision: room for later additions (event type, timings) without another breaking change |

## Open questions for review

**Q1 — Should `ScheduledActions` include the `ActionDefinition`?**
*Recommendation: yes.* Whoever dispatches a scheduled action later (your own scheduler, or a future Hangfire/Quartz
integration) needs its `Type` **and** `Parameters`. Without the definition, callers have to look it up again via the
rule ID and `ActionIndex`. The alternative is a smaller record with `RuleId`, `RuleName` and the `ActionResult`
only.

**Q2 — How should a rule with failed actions be reported?**
*Recommendation: `Matched` + `HasFailures`*, as above. The rule's own evaluation succeeded; the status describes the
rule, and action outcomes stay on the actions. The alternative is a fourth status, `PartiallyFailed` (or marking the
rule `Failed`), which is quicker to scan but mixes up the rule and its actions.

**Q3 — Should action identity be stamped onto `ActionResult`, or should results be wrapped?**
*Recommendation: stamp `ActionType`/`ActionIndex` onto `ActionResult`* (the engine applies `result with { ... }`,
overriding anything an executor set). Callers keep working with one result type. The alternative is a wrapper
`ActionOutcome(ActionType, ActionIndex, ActionResult Result)`, which keeps executor-created results untouched but adds
a level of nesting to every access (`rule.ActionResults[0].Result.Status`).

**Q4 — What happens to partial results when execution is cancelled?**
*Recommendation: they are discarded* — `OperationCanceledException` propagates, as it does today, which is the
standard .NET contract. Callers that need to know which actions ran before cancellation can't find out. The
alternative is to stop and return the partial result with a `Cancelled` marker, which is unusual and easy to
mistake for a completed run.

## Breaking changes (`feat!:`)

- `ExecuteAsync` (both overloads) returns `Task<RuleEngineResult>` instead of `Task<IReadOnlyList<RuleExecutionResult>>`.
  Migration: use `result.Rules` where the list was used.
- `RuleExecutionResult` gains `RuleId`, `Status` and `Error`; non-matching rules now appear with `NotMatched`.
  Code that assumed every entry matched must check `Status`.
- `ActionResult` gains `ActionType` and `ActionIndex`.
- Failures no longer appear on `Console.Error`.

## Test plan

- `RuleEngineTests`: one test per status (`Matched`, `NotMatched`, `Failed` with error); an action condition that
  throws fails only that action and later actions still run; results of earlier actions are present after a later
  failure; `ActionType`/`ActionIndex` are stamped, even over executor-set values; an executor returning `null` gives
  `Failed`; `HasFailures` for rule and action failures; `ScheduledActions` lists every scheduled action with its rule
  and definition; rules with another event type are excluded; cancellation before the first rule and between
  actions throws; nothing is written to `Console.Error` (redirected during the test).
- Existing engine, integration and sample code updated to `result.Rules`.

## Out of scope

- Structured logging / `ILogger` and observer events (observability backlog).
- Timing information on results.
- Actually dispatching scheduled actions (scheduler backends backlog).

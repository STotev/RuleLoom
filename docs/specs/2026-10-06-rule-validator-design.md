# Design: `RuleValidator`

**Issue:** [#39 API hardening: RuleValidator against registered operators and actions](https://github.com/STotev/RuleForge/issues/39)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleForge/issues/22) (module 7, user stories 41–43)
**Date:** 2026-10-06
**Status:** Approved (AFK)

---

## Problem

The parser (#31) checks a rule's *shape*, but whether its operators exist, have the right number of operands, and
whether its action types are registered is only discovered when the rule runs — after a rule-authoring UI has already
saved it. Rules built in code skip the parser entirely.

## API

```csharp
namespace RuleForge.Validation;

public sealed class RuleValidator
{
    public RuleValidator(IEnumerable<IConditionOperator> operators, IActionExecutorFactory actionFactory);

    // All problems at once; never throws for an invalid rule.
    public RuleValidationResult Validate(RuleDefinition rule);
}

public sealed record RuleValidationResult(IReadOnlyList<RuleValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed record RuleValidationError(string Path, string Message);
```

`IActionExecutorFactory` gains:

```csharp
IReadOnlyCollection<string> ActionTypes { get; }   // the action types this factory can resolve
```

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Inputs | The same operator list given to `ConditionEvaluator`, and the `IActionExecutorFactory` given to `RuleEngine` | No new registry concept; DI (#5) can build all three from one registration |
| Where | `RuleForge.Validation` in the `RuleForge` package | Validation needs registries; Abstractions only gains the factory member |
| Location format | JSONPath, as in `RuleParseException.Path`: `$.condition.operands[1]`, `$.actions[0].type` | One format for parse and validation errors (PRD) |
| Collect or throw | Collect every error; `Validate` never throws for invalid rules | UIs show all problems at once (PRD user story 42) |
| Checks | Rule `Name`/`EventType` non-empty; rule and action conditions are operators; every operator (recursively, including inside action conditions) is registered and its operand count is within `MinOperands`/`MaxOperands`; every action `Type` is non-empty and registered; `Delay`/`Advance` schedules have `Amount` and `Unit` | Everything that can be checked without knowing the event's data type. The schedule check was deferred here from #31 |
| Unknown operator | Its operands are still checked | Report every problem, not just the first |
| Name matching | Case-insensitive, like the evaluator and factory (#37) | |
| Duplicate operator names | `ArgumentException`, as in `ConditionEvaluator` | Same registry rules |
| Not checked | Property paths (and `$` variables) | Needs the event's type → Schema System (PRD Out of Scope). `$` variables in JSON are already checked by the parser once #38 lands |

## Breaking change (`feat!:`)

`IActionExecutorFactory` gains `ActionTypes`; custom factories must implement it. (#5's DI factory will.)

## Test plan

- Valid rules (including case-variant names) → `IsValid`.
- One test per error kind with its exact path and message; several errors in one rule reported together;
  errors inside nested operators and action conditions; unknown operator whose operands are also invalid.
- `DirectActionExecutorFactory.ActionTypes` lists registered types.

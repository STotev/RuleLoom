# RuleForge Architecture Plan: Operator API Redesign

## Problem

`IConditionOperator` exposes too much plumbing. Operator authors must understand three types from two
namespaces (`EvaluationContext`, `IReadOnlyList<ConditionNode>`, `IConditionEvaluator`), and the
`Evaluate()` vs `ResolveValue()` split on `IConditionEvaluator` is unclear — you have to guess which
one to call for each operand. Complex operators like `changed` are hand-coded special cases with no
composition story; there is no clear pattern for external plugin authors to build operators from
existing primitives. Additionally, `RuleJsonParser.Parse()` produces structurally invalid
`RuleDefinition` objects (e.g. `PathOperand` as root condition) that only blow up at execution time.

---

## Change 1 — `OperatorContext`

> **Breaking change** in `IConditionOperator`. Acceptable pre-1.0.

Introduce `OperatorContext` in `RuleForge.Abstractions.Condition`. Replace the 3-param signature:

```csharp
// BEFORE
bool Evaluate(EvaluationContext ctx, IReadOnlyList<ConditionNode> operands, IConditionEvaluator evaluator);

// AFTER
bool Evaluate(OperatorContext ctx);
```

`OperatorContext` exposes only what operators actually need:

```csharp
public sealed class OperatorContext
{
    // For value operators (eq, gt, contains): resolve operand to its value
    public object? GetValue(int index);

    // For composite operators (and, or, not): evaluate operand as boolean
    public bool Test(int index);

    // For operators that need the path string itself (changed, future path-aware ops)
    // Throws InvalidOperationException if operand at index is not a PathOperand
    public string GetPath(int index);

    public int OperandCount { get; }

    // Resolve an arbitrary path against current/previous
    public object? Resolve(string path);

    // Advanced: evaluate an ad-hoc condition node inline — enables composition from primitives
    public bool Test(ConditionNode node);
}
```

`IConditionEvaluator` and `EvaluationContext` become internal implementation details inside
`ConditionEvaluator`. No operator author ever imports or interacts with them.

### Built-in operators after the change

```csharp
// eq — intent obvious: get two values, compare
public bool Evaluate(OperatorContext ctx) =>
    Equals(ctx.GetValue(0), ctx.GetValue(1));

// and — intent obvious: test each operand as a boolean condition
public bool Evaluate(OperatorContext ctx)
{
    for (var i = 0; i < ctx.OperandCount; i++)
        if (!ctx.Test(i)) return false;
    return true;
}

// or
public bool Evaluate(OperatorContext ctx)
{
    for (var i = 0; i < ctx.OperandCount; i++)
        if (ctx.Test(i)) return true;
    return false;
}

// not
public bool Evaluate(OperatorContext ctx) => !ctx.Test(0);

// changed — now clearly: "I need the path string, not the resolved value"
public bool Evaluate(OperatorContext ctx)
{
    var path = ctx.GetPath(0);
    return !Equals(ctx.Resolve($"current.{path}"), ctx.Resolve($"previous.{path}"));
}
```

### Composition story — `changed` expressed from primitives

`ctx.Test(ConditionNode)` enables operators to compose from existing primitives without
reimplementing their logic. This is the blueprint for external plugin authors:

```csharp
// changed IS semantically not(eq(current.X, previous.X))
// This version delegates entirely to existing operators via composition
public bool Evaluate(OperatorContext ctx)
{
    var path = ctx.GetPath(0);
    return ctx.Test(new OperatorCondition
    {
        Operator = "not",
        Operands =
        [
            new OperatorCondition
            {
                Operator = "eq",
                Operands =
                [
                    new PathOperand { Path = $"current.{path}" },
                    new PathOperand { Path = $"previous.{path}" }
                ]
            }
        ]
    });
}
```

### Writing a custom operator with the new API

A plugin author building a `contains` operator for strings needs zero knowledge of the evaluator:

```csharp
public class ContainsOperator : IConditionOperator
{
    public string Name => "contains";
    public int MinOperands => 2;
    public int MaxOperands => 2;

    public bool Evaluate(OperatorContext ctx)
    {
        var haystack = ctx.GetValue(0)?.ToString() ?? string.Empty;
        var needle   = ctx.GetValue(1)?.ToString() ?? string.Empty;
        return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
```

Compare to today's equivalent — same logic but with unrelated infrastructure in the signature.

---

## Change 2 — Parse-time structural validation

`RuleJsonParser.Parse()` and `ParseMany()` validate structure immediately after deserialisation.
The engine always receives valid rules — no structural exceptions at execution time.

### What is validated at parse time

| Rule | Example violation |
|---|---|
| `Condition` must not be null | Rule has no `condition` property |
| Root condition must be `OperatorCondition` | `"condition": { "path": "Price" }` |
| `OperatorCondition.Operator` must not be null or whitespace | `"operator": ""` |
| Every nested `OperatorCondition` satisfies the above (recursive) | Nested operator with no name |
| `OperatorCondition` must have at least one operand | `"operator": "eq"` with no `operands` |

### Examples of errors that throw at parse time (not execution time)

```csharp
// PathOperand as root → throws
RuleJsonParser.Parse("""{"name":"X","eventType":"E","condition":{"path":"Price"}}""");
// ParseException: Rule condition must be an operator node

// Empty operator name → throws
RuleJsonParser.Parse("""{"name":"X","eventType":"E","condition":{"operator":""}}""");
// ParseException: Operator name cannot be empty

// Operator with no operands → throws
RuleJsonParser.Parse("""{"name":"X","eventType":"E","condition":{"operator":"eq","operands":[]}}""");
// ParseException: Operator 'eq' must have at least one operand
```

### What is NOT validated at parse time (future semantic validation phase)

- Operator name exists in the registered operator registry
- Operand count matches `MinOperands`/`MaxOperands` for the specific operator
- Path strings refer to valid properties on a known type

These require operator registry access at validation time and are planned as a separate phase.

---

## Implementation Order

Dependencies are listed per task. Steps 3a, 3b, and 4 can be done in parallel.

```
1. op-context-type       Add OperatorContext to Abstractions
        ↓
2. op-interface-update   Update IConditionOperator.Evaluate signature
       ↙         ↘
3a. op-update-builtins   3b. evaluator-update     4. parser-validation
Update 5 operators       Update ConditionEvaluator  Add structural validation
       ↘         ↙               ↙
5. update-tests   Update/add tests for all changes
        ↓
6. update-samples   Update sample code
```

## Branch

`refactor/operator-context-api`

## Commit sequence (Conventional Commits)

```
refactor: introduce OperatorContext and simplify IConditionOperator.Evaluate
refactor: update built-in operators to use OperatorContext
refactor: update ConditionEvaluator to construct OperatorContext internally
feat: add structural validation to RuleJsonParser
test: update operator and evaluator tests for new OperatorContext API
test: add parse-time validation tests to RuleJsonParserTests
```

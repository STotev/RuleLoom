# Design: OperatorContext Operator API

**Issue:** [#34 API hardening: OperatorContext operator API](https://github.com/STotev/RuleLoom/issues/34) (also closes #17)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleLoom/issues/22) (modules 2–4)
**Supersedes:** the March 2026 operator-context proposal (no longer in the repository), with the revisions agreed in PRD #22
**Date:** 2026-10-05
**Status:** Approved 2026-10-05 (HITL review; Q1–Q4 resolved as recommended)

---

## Problem

Operator authors implement

```csharp
bool Evaluate(EvaluationContext context, IReadOnlyList<ConditionNode> operands, IConditionEvaluator evaluator);
```

and must know three types from two namespaces. For every operand they have to guess whether to call
`evaluator.Evaluate` (as a condition) or `evaluator.ResolveValue` (as a value), and they must re-implement value
comparison (or forget to; see #30). Errors are unclear: registering two operators with the same name throws a bare
`ArgumentException: An item with the same key has already been added`, and a path or value where a condition is
expected fails with `Root must be operator` even when the node isn't the root (#17).

## Proposed API

```csharp
namespace RuleLoom.Abstractions.Condition;

public interface IConditionOperator
{
    string Name { get; }
    int MinOperands { get; }
    int MaxOperands { get; }

    bool Evaluate(OperatorContext context);          // replaces the three-parameter overload
}

public sealed class OperatorContext
{
    // For tests and custom evaluators. The evaluator is only needed to Test nested operator operands (Q3).
    public OperatorContext(OperatorCondition condition, EvaluationContext evaluationContext,
        IConditionEvaluator? evaluator = null);

    public string OperatorName { get; }               // condition.Operator, used in error messages
    public int OperandCount { get; }

    public object? GetValue(int index);               // value operand → its value; path → resolved value;
                                                      // operator → its boolean result
    public bool Test(int index);                      // operand as a condition; evaluated only when called (Q2)
    public string GetPath(int index);                 // the path string; descriptive error if not a path operand

    public object? Resolve(string path);              // resolve any path against the current/previous state
    public EvaluationContext EvaluationContext { get; }

    public bool AreEqual(object? left, object? right);                  // ValueComparer.AreEqual
    public bool TryCompare(object? left, object? right, out int result); // ValueComparer.TryCompare
}

public interface IConditionEvaluator
{
    bool Evaluate(EvaluationContext context, ConditionNode node);       // ResolveValue is removed (Q1)
}
```

### Built-in operators after the change

```csharp
// eq
public bool Evaluate(OperatorContext ctx) => ctx.AreEqual(ctx.GetValue(0), ctx.GetValue(1));

// and (or is the mirror image)
public bool Evaluate(OperatorContext ctx)
{
    for (var i = 0; i < ctx.OperandCount; i++)
        if (!ctx.Test(i)) return false;
    return true;
}

// not
public bool Evaluate(OperatorContext ctx) => !ctx.Test(0);

// changed: semantics unchanged here; the $current/$previous prefixes and scoped paths are #38
public bool Evaluate(OperatorContext ctx)
{
    var path = ctx.GetPath(0);
    return !Equals(ctx.Resolve($"current.{path}"), ctx.Resolve($"previous.{path}"));
}
```

### A custom operator and its test

```csharp
public sealed class ContainsOperator : IConditionOperator
{
    public string Name => "contains";
    public int MinOperands => 2;
    public int MaxOperands => 2;

    public bool Evaluate(OperatorContext ctx) =>
        ctx.GetValue(0) is string text && ctx.GetValue(1) is string part &&
        text.Contains(part, StringComparison.Ordinal);
}

[Fact]
public void Evaluate_TextContainsPart_ReturnsTrue()
{
    var condition = new OperatorCondition
    {
        Operator = "contains",
        Operands = [new PathOperand { Path = "Title" }, new ValueOperand { Value = "sale" }]
    };
    var ctx = new OperatorContext(condition, new EvaluationContext(new { Title = "Summer sale" }));

    Assert.True(new ContainsOperator().Evaluate(ctx));
}
```

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Composition via `Test(ConditionNode)` (from the March proposal) | Dropped | PRD #22: builds operator trees from strings on every evaluation and adds little for plugin authors |
| `EvaluationContext` | Stays public, and is exposed on `OperatorContext` | Actions and scheduling receive it; operators may need it for advanced cases |
| Where `OperatorContext` lives | `RuleLoom.Abstractions.Condition` | Next to `IConditionOperator`; depends only on Abstractions types (`EvaluationContext`, `ValueComparer`) |
| `OperatorContext` constructor takes the `OperatorCondition` | Yes, not a raw operand list | Gives the operator name for error messages and keeps the constructor short |
| Class or struct | `sealed class` | One small allocation per operator evaluation is negligible next to path resolution; performance is measured in #27 |
| Operand count validation | Still done by `ConditionEvaluator` before calling `Evaluate`, so `GetValue`/`Test`/`GetPath` can assume valid indexes up to `MinOperands` | Unchanged behaviour |
| Out-of-range index | `ArgumentOutOfRangeException` | Programming error in the operator |
| Duplicate operator names (#17) | `ArgumentException` naming the duplicate, e.g. `Operator 'eq' is registered more than once.` | |
| Non-operator node passed to `ConditionEvaluator.Evaluate` (#17) | `InvalidOperationException` naming the node type, e.g. `Expected an operator condition but got PathOperand.`; no mention of "root" | The parser (#31) already rejects non-operator roots, so this is mostly reached via `Test(i)` |
| `changed` | Same semantics; `GetPath(0)` gives a clear error for non-path operands | `$current`/`$previous` and scoped paths are #38 (folds in #16) |
| The March proposal | Archived (no longer in the repository) | Superseded |

## Resolved questions

All four were reviewed on 2026-10-05 and resolved as recommended.

**Q1 — Keep `IConditionEvaluator` public, reduced to `Evaluate`?**
*Resolved: yes.* `RuleEngine` depends on it, tests fake it, and DI (#5) registers it. `ResolveValue` is only
used by operators, which now use `OperatorContext.GetValue`, so it's removed. The alternative is to make the
interface internal and have `RuleEngine` take `ConditionEvaluator`, which removes a public type but also the ability
to substitute the evaluator.

**Q2 — What does `Test(i)` do with a path or value operand?**
*Resolved: accept it when the value is a `bool`*, so `{"operator": "and", "operands": [{"path": "IsActive"},
{...}]}` works, and throw a descriptive `InvalidOperationException` otherwise (e.g. `Operator 'and' expects a
condition at operand 0, but path 'Name' resolved to String.`). The alternative is to require operator operands only,
which is stricter but forces `{"operator": "eq", "operands": [{"path": "IsActive"}, {"value": true}]}` for simple
flags.

**Q3 — How do plugin authors build an `OperatorContext` in tests?**
*Resolved: the public constructor above*, with the evaluator optional: tests for value operators need no
evaluator, and tests for composite operators pass any `IConditionEvaluator` (e.g. a real `ConditionEvaluator`). The
alternative is a separate test-helper package or factory, which is more to maintain for little gain. *Maintainer note:* a test-helpers package for consumers may be
reconsidered later (recorded in the roadmap backlog).

**Q4 — Should `OperatorContext` expose the raw operand nodes?**
*Resolved: no, not now.* `GetValue`, `Test` and `GetPath` cover the built-in and planned operators, and hiding
the nodes is the point of the redesign. If a real operator needs to inspect operand kinds, an `GetOperandKind(i)` (or
similar) can be added later without breaking anything. The alternative is an `Operands` property, which is flexible
but brings back the plumbing.

## Breaking changes (`feat!:`)

- `IConditionOperator.Evaluate(EvaluationContext, IReadOnlyList<ConditionNode>, IConditionEvaluator)` →
  `Evaluate(OperatorContext)`. Migration: `evaluator.ResolveValue(ctx, operands[i])` → `ctx.GetValue(i)`;
  `evaluator.Evaluate(ctx, operands[i])` → `ctx.Test(i)`; `operands.Count` → `ctx.OperandCount`;
  `(operands[i] as PathOperand).Path` → `ctx.GetPath(i)`; `Equals(a, b)` → `ctx.AreEqual(a, b)`.
- `IConditionEvaluator.ResolveValue` is removed (Q1).

## Test plan

- `OperatorContextTests`: `GetValue` for each node kind; `Test` laziness (counting operator), bool paths and values,
  descriptive errors for non-bool values; `GetPath` success and error; `Resolve`; `AreEqual`/`TryCompare` delegate to
  `ValueComparer`; out-of-range index; nested operator without an evaluator gives a clear error.
- Operator tests (`EqualsOperatorTests`, `ChangedOperatorTests`, `And`/`Or`/`NotOperatorTests`) call operators
  through a test-constructed `OperatorContext` where they test the operator alone, and keep the evaluator-level
  tests that cover registration and nesting.
- `ConditionEvaluatorTests`: duplicate-name message, non-operator node message, `ResolveValue` tests replaced by
  `OperatorContext.GetValue` tests.
- A custom operator, written and unit-tested exactly as in the example above, to prove the plugin-author path.

## Out of scope

- `gt`/`gte`/`lt`/`lte` (#36), case-insensitive operator names (#37), `$current`/`$previous` (#38).
- Composing operators from other operators.

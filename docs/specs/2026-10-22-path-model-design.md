# Design: Parsed Rule Paths and a Structured Validator

**Issue:** [#107 Parse rule paths once and restructure RuleValidator](https://github.com/STotev/RuleLoom/issues/107)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87)
**Date:** 2026-10-22
**Status:** Approved

---

## Problem

Six places take the path syntax apart by hand, each with its own `Split('.')`, `StartsWith('@')` and `$current`
comparisons:

| Where | What it does with the string |
|---|---|
| `RuleJsonValidator.ValidatePathVariables` | parse-time errors: `#`, bare `@`, unknown `$` variables, a context after a state |
| `EvaluationContext.Resolve` | picks the root (context, `$current`, `$previous`, subject) |
| `ContextReferences.AddPath` | finds `@name` to prefetch |
| `ChangedOperator` | strips `$current.`/`$previous.`, rejects contexts, rebuilds `$current.x`/`$previous.x` |
| `RuleValidator.ValidatePath` | repeats the parser's errors for rules built in code |
| `SchemaPathChecker.Resolve` | picks the root again, then walks the segments |

They already disagree. A `#` path or `$previous.@customer` built in code resolves silently to `null` in the engine,
while the parser and the validator reject it. Every new piece of syntax (functions are reserved with `#`) would have to
be taught to all six.

The validator is procedural, and `SchemaPathChecker` walks the rule tree a second time.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Path model | A public **`RulePath`** in `RuleLoom.Abstractions`: `Root` (`PathRoot.Current`, `Previous`, `Context`), `Context` (the name, for context paths), `Property` (the dotted rest; empty for the whole state or context) and `Segments`. `"Price"` and `"$current.Price"` both have root `Current` | One grammar and one set of error messages. The engine, operators and the validator only need these parts |
| Why public | `RuleLoom` (engine, operators, validator) needs it, and it lives in Abstractions next to the parser and `EvaluationContext`. `InternalsVisibleTo` across packages breaks when the two packages are mixed at different versions | Kept small: a parsed value with no behaviour. It also gives custom operators a supported way to read paths instead of parsing strings |
| Parsing | `RulePath.TryParse(path, out result, out error)` and `RulePath.Parse(path)` (throws `FormatException`). Errors (same messages as today): `#` paths reserved for functions, a bare `@`, unknown `$` variables, a context after a state. Parsed paths are cached by text, like `PathResolver`'s accessors | Each caller wraps the error in its own exception type (`RuleParseException`, `InvalidOperationException`) or validation error |
| `PathOperand.Path` | Stays a public `string`; callers parse it through `RulePath` | No API change for rule authors and rule JSON |
| Walking properties | `PathResolver` and `JsonPathWalker` keep taking the property path (`RulePath.Property`) | They walk properties, not roots; their caches are keyed by it |
| Users | The parser (`RuleJsonValidator`), `EvaluationContext.Resolve`, `ContextReferences`, `ChangedOperator` and the validator all use `RulePath`; no other code splits path strings | The acceptance criterion of #107 |
| Validator structure | One internal walk over the rule (`RuleWalker`): the rule's fields, root conditions, operators, path operands (parsed once, with their parent operator), actions and schedules (including `referencePath`). It hands each node to small, generic checks: rule fields, root conditions, operators (unknown, operand counts, then the operator's own operand validation), actions and schedules, path syntax, and with schemas: schema paths. **No check names a specific operator** | Each rule lives in one place; `Validate` and `ValidateAsync` share the walk and the checks instead of repeating them; built-in and custom operators are validated the same way |
| Operator-specific validation | Operators validate their own operands: `IConditionOperator.ValidateOperands(OperandValidationContext)`, a default interface method that checks nothing. The context gives the operands, their parsed paths, their schema fields (with schemas) and `Error`/`Warning` (for an operand or the whole operator). `changed` reports contexts, whole states and non-path operands, sharing one rule with `Evaluate`; `eq`/`gt`/`gte`/`lt`/`lte` report types that can't match, through one shared helper | Open/closed: like `MinOperands`/`MaxOperands`, the operator declares what it accepts and the validator enforces it generically; custom operators get the same hook; no rule is stated twice |
| `ValidateAsync` | Fetches the event and context schemas first, then runs one walk with the base checks plus the schema-path check. Operators' validation gets the fields that check resolved (once per path) | No second traversal |
| Behaviour | Validation messages and locations are unchanged. Errors are reported in **document order** (`RuleValidationResult` already says so); before, `ValidateAsync` appended schema errors after the others | One walk gives document order naturally |

### Behaviour changes (small, deliberate)

- **The engine rejects invalid paths like the parser does.** `EvaluationContext.Resolve` now throws
  `InvalidOperationException` for every syntax error (`#` paths, a bare `@`, a context after a state), not only for
  unknown `$` variables. The rule fails instead of silently reading `null`. Only rules built in code can contain these,
  because the parser already rejects them.
- **Validation errors come in document order**, so they no longer group as "base errors, then schema errors".

## API

```csharp
namespace RuleLoom.Abstractions;

public enum PathRoot { Current, Previous, Context }

public sealed record RulePath
{
    public PathRoot Root { get; }
    public string? Context { get; }              // the context name for PathRoot.Context
    public string Property { get; }              // "Customer.Tier"; empty for the whole state or context
    public IReadOnlyList<string> Segments { get; }

    public static RulePath Parse(string path);   // FormatException with the parse error
    public static bool TryParse(string path, [NotNullWhen(true)] out RulePath? result, [NotNullWhen(false)] out string? error);
}

namespace RuleLoom.Abstractions.Condition;

public interface IConditionOperator
{
    // Name, MinOperands, MaxOperands, Evaluate as before
    void ValidateOperands(OperandValidationContext context) { }   // new; called with a valid operand count
}

public sealed class OperandValidationContext
{
    public OperandValidationContext(OperatorCondition condition, Func<int, FieldDefinition?>? fields = null);

    public string OperatorName { get; }
    public int OperandCount { get; }
    public ConditionNode GetOperand(int index);
    public RulePath? GetPath(int index);           // a valid path operand, else null
    public FieldDefinition? GetField(int index);   // its schema field, when validating with schemas
    public void Error(int index, string message);  // at the operand (".path" for path operands)
    public void Error(string message);             // at the operator
    public void Warning(int index, string message);
    public void Warning(string message);
    public IReadOnlyList<OperandProblem> Problems { get; }
}

public sealed record OperandProblem(int? Operand, string Message, bool IsWarning);
```

## Test plan

- **`RulePath`:** each root (`Price`, `$current`, `$previous.Price`, case-insensitive variables, `@customer`,
  `@customer.Tier`), nested properties, and each error with its message.
- **Unchanged behaviour:** the existing parser, evaluator, context, `changed` and validator tests pass as they are,
  except for the one validator test whose expected error order becomes document order.
- **Engine:** `#` paths, a bare `@` and `$previous.@x` in rules built in code fail the rule with the parse message.

## Docs

- JSON guide: paths section mentions `RulePath` for custom operators.
- `AGENTS.md`: key concepts (paths) and extension points (operators read paths with `RulePath`).
- Roadmap backlog: review the packages' layering before 1.0 (Abstractions holds implementation such as the parser,
  `PathResolver`, `EvaluationContext` and value comparison, and references Schemas; an abstractions package is usually
  the one others reference).

## Deviations

- `ChangedOperator` needed the same property in both states without rebuilding `"$current.x"` strings, so the API gained
  `RulePath.InState(PathRoot)` and `Resolve(RulePath)` overloads on `EvaluationContext` and `OperatorContext` (the
  string overloads parse and delegate).
- `RuleWalker` gives each check a schedule hook (after the action's condition, before its `referencePath`), so errors
  stay in document order within an action.
- Checks are grouped by what they check: `ActionCheck` covers actions and their schedules.
- After review of the first implementation: `ChangedCheck` and `ComparisonTypeCheck` named built-in operators inside the
  validator and repeated `ChangedOperator`'s errors. They were replaced by operators validating their own operands
  (*Operator-specific validation* above). `changed` also reports a non-path operand now.
- After code review:
  - The walker gives checks a second operator hook after the operands (with the operands' parsed paths), so
    `ComparisonTypeCheck` runs after `SchemaPathCheck` has resolved and reported them: errors stay in document order.
    `SchemaPaths` memoizes per path node, not per location string.
  - `changed` on a whole state (`$current`, `$previous`) is an error in the validator and the engine: it compared the
    two state objects (always "changed" for two instances); before #107 it read a non-existent property.
  - `ChangedOperator` throws `InvalidOperationException` for invalid paths, like `EvaluationContext.Resolve`.
  - The parse cache keeps only valid paths, up to 10,000, since paths can come from rules submitted at run time.
  - `Segments` is computed once; `RulePath` equality compares `Root`, `Context` and `Property`.
  - **Breaking:** `EvaluationContext.Resolve(string)` (public since 0.5) throws for invalid paths it used to read as
    `null` (`#…`, a bare `@`, a context after a state), so the PR is marked `feat!`.
  - Not changed: `InState` allocates a small `RulePath` per `changed` evaluation; the old code built and split two
    strings.

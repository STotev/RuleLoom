# Upgrading RuleForge

RuleForge is pre-1.0, so minor versions can contain breaking changes. This page lists every breaking change, newest
first, with what to change. Each version's GitHub release notes link the pull requests with the full details.

- [0.3 → 0.4](#03--04): actions are named by registration, `IAction`, dependency injection
- [0.2 → 0.3](#02--03): API hardening (results, operators, validation, paths, parameters)
- [0.1 → 0.2](#01--02): action results, action factory, scheduling, the RuleForge rename

---

## 0.3 → 0.4

### `IActionExecutor` is now `IAction`, and actions no longer declare their name

Rules refer to an action by the name it's **registered** under. By default that's the class name without an `Action`
suffix, in kebab-case (`SendEmailAction` → `send-email`). `[ActionName("...")]` on the class changes the default, and
a name given at registration overrides both.

```csharp
// Before
public class SendEmailAction : IActionExecutor
{
    public string Type => "send-email";
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken ct = default) { ... }
}

// After: "send-email" by convention
public class SendEmailAction : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken ct = default) { ... }
}

// If the old Type doesn't match the class name, keep it with the attribute...
[ActionName("email")]
public class SendEmailAction : IAction { ... }

// ...or choose the name where you register it
var actions = new DirectActionFactory().Add(new SendEmailAction(), "email");
```

| Before | After |
|---|---|
| `IActionExecutor` | `IAction` |
| `IActionExecutorFactory` | `IActionFactory` |
| `DirectActionExecutorFactory` | `DirectActionFactory` (`new DirectActionFactory(actions)` still works; `.Add(action, name?)` is new) |
| `factory.TryGetExecutor(name, out executor)` | `factory.TryGetAction(name, out action)` |
| `factory.ActionTypes` | `factory.ActionNames` |

### Rules reference actions with `"action"` instead of `"type"`

```json
// Before
{ "type": "send-email", "parameters": { "to": "ada@example.com" } }

// After
{ "action": "send-email", "parameters": { "to": "ada@example.com" } }
```

In code: `ActionDefinition.Type` → `ActionDefinition.Action`, and `ActionResult.ActionType` → `ActionResult.ActionName`.

### New: dependency injection (not breaking)

```csharp
services.AddRuleForge(rules => rules.AddAction<SendEmailAction>());
```

See the [quick start](quick-start.md#dependency-injection).

---

## 0.2 → 0.3

### `ExecuteAsync` returns a `RuleEngineResult`

```csharp
// Before
IReadOnlyList<RuleExecutionResult> results = await engine.ExecuteAsync(eventType, current, previous, rules);

// After
RuleEngineResult result = await engine.ExecuteAsync(eventType, current, previous, rules);
foreach (var rule in result.Rules) { ... }   // rule.Status: Matched, NotMatched or Failed
```

- Rules whose condition is false now appear with `Status == NotMatched` (they used to be left out). Check `Status` if
  you assumed every entry matched.
- A rule whose condition can't be evaluated has `Status == Failed` and `Error`, instead of disappearing.
- A failing action condition fails only that action; other actions still run.
- Every action result has `ActionName` (0.4; `ActionType` in 0.3) and `ActionIndex`. `result.ScheduledActions` lists
  scheduled actions with their definitions; `result.HasFailures` tells you if anything failed.
- Nothing is written to `Console.Error` any more.

### Operators implement `Evaluate(OperatorContext)`

```csharp
// Before
public bool Evaluate(EvaluationContext ctx, IReadOnlyList<ConditionNode> operands, IConditionEvaluator evaluator)
{
    var left = evaluator.ResolveValue(ctx, operands[0]);
    var right = evaluator.ResolveValue(ctx, operands[1]);
    return Equals(left, right);
}

// After
public bool Evaluate(OperatorContext ctx) => ctx.AreEqual(ctx.GetValue(0), ctx.GetValue(1));
```

| Before | After |
|---|---|
| `evaluator.ResolveValue(ctx, operands[i])` | `ctx.GetValue(i)` |
| `evaluator.Evaluate(ctx, operands[i])` | `ctx.Test(i)` |
| `operands.Count` | `ctx.OperandCount` |
| `((PathOperand)operands[i]).Path` | `ctx.GetPath(i)` |
| `Equals(a, b)` | `ctx.AreEqual(a, b)` (shared comparison rules) |
| `IConditionEvaluator.ResolveValue` | removed |

### Paths select the previous state with `$previous.`

```json
// Before
{ "path": "previous.Price" }

// After
{ "path": "$previous.Price" }
```

`current.`/`previous.` without `$` are now ordinary property names, and other names starting with `$` are reserved.

### Rules are validated when parsed

`RuleJsonParser` rejects invalid rules with a `RuleParseException` (a `JsonException`) whose `Path` says where the
problem is. Rules that used to parse but are now rejected:

| Before | After |
|---|---|
| `"condition": { "path": "IsActive" }` | `"condition": { "operator": "eq", "operands": [{ "path": "IsActive" }, { "value": true }] }` |
| missing or empty `name` / `eventType` | required (also `required` members in C#) |
| `{}` as a condition node | `{ "value": null }` |
| `{ "operator": "eq", "path": "X" }` | one of `operator`, `path`, `value` per node |

To check operators and actions before saving a rule, use `RuleValidator` (new).

### Values are compared by meaning; names ignore case

- `eq` (and the new `gt`, `gte`, `lt`, `lte`) compare across types: `Price eq 100` matches a `decimal`, `"active"`
  matches an enum, and ISO 8601 strings match dates. Rules that silently didn't match before may match now.
- Operator names, action names, event types and property paths ignore case. Registering two operators or actions whose
  names differ only in case now throws.

### Action parameters are plain values

Parameters parsed from JSON are no longer `JsonElement`. Read them with `GetParameter<T>`:

```csharp
// Before
var amount = ((JsonElement)action.Parameters["amount"]!).GetDecimal();

// After
var amount = action.GetParameter<decimal>("amount");
```

### Schedule reference dates must be ISO 8601

A `ReferencePath` that resolves to a string must be ISO 8601 (`2026-05-01T09:00:00Z`, `2026-05-01`). Other formats
(`04/02/2026`) now give a `Failed` action result.

---

## 0.1 → 0.2

### The library is renamed to RuleForge

| Before | After |
|---|---|
| `BusinessRules.Abstractions.*` | `RuleForge.Abstractions.*` |
| `BusinessRules.Core.*` | `RuleForge.*` (e.g. `RuleForge.Engine`, `RuleForge.Operators`) |

### Actions return a result

```csharp
// Before
public Task ExecuteAsync(ActionDefinition action, EvaluationContext context) { ...; return Task.CompletedTask; }

// After (0.4 names shown)
public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken ct = default)
{
    ...
    return Task.FromResult(ActionResult.Success());
}
```

### `RuleEngine` takes an action factory

```csharp
// Before
var engine = new RuleEngine(evaluator, new IActionExecutor[] { new LogAction() });

// After (0.4 names shown)
var engine = new RuleEngine(evaluator, new DirectActionFactory([new LogAction()]));
```

### `ExecuteAsync` returns results

`ExecuteAsync` returned `Task`; it now returns the results (a `RuleEngineResult` since 0.3, see above).

### Scheduled actions aren't executed

Actions with a `Delay` or `Advance` schedule return a `Scheduled` result with `ScheduledFor` instead of running.
Dispatching them at that time is up to your scheduler.

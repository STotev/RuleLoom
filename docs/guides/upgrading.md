# Upgrading RuleLoom

RuleLoom is pre-1.0, so minor versions can contain breaking changes. This page lists every breaking change, newest
first, with what to change. Each version's GitHub release notes link the pull requests with the full details.

- [0.4 → 0.5](#04--05): rule ids are required; `LogAction` is `ConsoleLogAction`; ISO date strings and `changed` compare by meaning; rules on JSON state
- [0.3 → 0.4](#03--04): actions are named by registration, `IAction`, dependency injection
- [0.2 → 0.3](#02--03): API hardening (results, operators, validation, paths, parameters)
- [0.1 → 0.2](#01--02): action results, action factory, scheduling

---

## 0.4 → 0.5

### Rules need an `id`

A rule's `id` is required: it used to default to a new GUID on every parse, so a rule without one got a different
identity each time it was loaded. JSON rules without an `id` (or with an empty or non-string one) now throw
`RuleParseException` at `$.id`, and `new RuleDefinition { ... }` without `Id` doesn't compile.

```json
{ "id": "vip-order", "name": "VIP order", "eventType": "OrderPlaced", "condition": { ... } }
```

What to change: give every rule a stable `id`, for example its key in your rule store. Results
(`RuleExecutionResult.RuleId`) and scheduled actions (`ScheduledAction.RuleId`) carry it, so it can identify the rule
in your logs and job keys.

### `LogAction` is now `ConsoleLogAction` (`console-log`)

The built-in action that writes `message` to the console is renamed to say what it does; it's meant for development
and testing. Its default name follows: `console-log` instead of `log`. It's still not registered by `AddRuleLoom`.

```csharp
// 0.4
services.AddRuleLoom(rules => rules.AddAction<LogAction>());            // "log"

// 0.5
services.AddRuleLoom(rules => rules.AddAction<ConsoleLogAction>());     // "console-log"
services.AddRuleLoom(rules => rules.AddAction<ConsoleLogAction>("log")); // or keep the old name
```

What to change: rename the type, and either change `"action": "log"` to `"console-log"` in your rules or register it
under `"log"` as above (`new DirectActionFactory().Add(new ConsoleLogAction(), "log")` without DI).

### Two ISO 8601 strings compare as dates

When **both** values are strict ISO 8601 strings (typical for JSON state, where dates are strings), `eq`, `gt`, `gte`,
`lt` and `lte` compare them as instants instead of as text:

| Comparison | 0.4 | 0.5 |
|---|---|---|
| `"2026-05-01T14:00:00+02:00"` eq `"2026-05-01T12:00:00Z"` | false | **true** |
| `"2026-01-01"` eq `"2026-01-01T00:00:00Z"` | false | **true** (date-only means UTC midnight) |
| `"2026-06-01T01:30:00+02:00"` lt `"2026-06-01"` | false (text order) | **true** (23:30Z on 31 May) |
| `"2026-05-01"` lt `"May 1st"` | true (text order) | **false**: a date against other text can't be ordered |

What to check: rules that compare string properties holding dates. If you relied on text order, compare against a
`DateTime`/`DateTimeOffset` property or an ISO value instead.

### `changed` compares by meaning

`changed` now uses the same equality as `eq`, so it no longer fires when only the type or format differs:

- `100` → `100.0` (e.g. `long` vs `decimal`), or `"2026-05-01T14:00:00+02:00"` → `"2026-05-01T12:00:00Z"`: not a change.
- JSON objects and arrays (paths ending at an object, since rules can run on JSON state) compare by content,
  ignoring property order.
- Other objects are compared with `Equals`, as before.

What to check: rules using `changed` on properties whose type differs between current and previous state.

### New: rules on JSON state (not breaking)

Rules can run on a `JsonElement` or `JsonNode` instead of a .NET class, and paths follow the runtime value of
properties declared as `object` or an interface (they used to resolve to `null`). See
[Rules on JSON State](json-rules.md#rules-on-json-state).

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
services.AddRuleLoom(rules => rules.AddAction<SendEmailAction>());
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

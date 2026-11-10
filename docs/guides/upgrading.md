# Upgrading RuleLoom

RuleLoom is pre-1.0, so minor versions can contain breaking changes. This page lists every breaking change, newest
first, with what to change. Each version's GitHub release notes link the pull requests with the full details.

- [0.7 → 0.8](#07--08): `ValueComparer` and `ValueConverter` take `TemporalRules`; impossible `Local` times are rejected
- [0.6 → 0.7](#06--07): a parameter `{ "$path": ... }` is a path; `FieldDefinition` has `IsRequired`; optionally, typed actions, path parameters and `MapRuleLoom`
- [0.5 → 0.6](#05--06): the engine, validator and DI run on a catalog; paths starting with `@` or `#` are no longer property names
- [0.4 → 0.5](#04--05): rule ids are required; `LogAction` is `ConsoleLogAction`; ISO date strings and `changed` compare by meaning; rules on JSON state
- [0.3 → 0.4](#03--04): actions are named by registration, `IAction`, dependency injection
- [0.2 → 0.3](#02--03): API hardening (results, operators, validation, paths, parameters)
- [0.1 → 0.2](#01--02): action results, action factory, scheduling

---

## 0.7 → 0.8

### `ValueComparer` and `ValueConverter` take `TemporalRules`

Date-times without an offset are now read in the catalog's time zone (`CatalogBuilder.UseTimeZone`, UTC by default), so
the comparison and conversion methods need to know it: `ValueComparer.AreEqual(left, right, temporalRules)`,
`ValueComparer.TryCompare(left, right, temporalRules, out result)` and `ValueConverter.TryConvert(value, type,
temporalRules, out result)` (and `TryConvert<T>(value, temporalRules, out result)`). In operators, keep using
`ctx.AreEqual`/`ctx.TryCompare`, which pass the evaluation's rules; elsewhere pass `context.TemporalRules`,
`catalog.TemporalRules`, or `TemporalRules.Utc` for the old behaviour. Without `UseTimeZone`, results don't change.

### Date-times the clocks skip are invalid

A wall-clock time that can't exist in its zone (skipped when daylight saving starts) now fails the rule or action
that reads it with an `InvalidTemporalValueException`, instead of being read with a guessed offset. For `Unspecified`
values and text this only happens once a time zone is configured; `DateTime` values with `Kind = Local` are checked
against the machine's time zone, so such values are rejected now. Typed actions also read date-time parameters without
an offset in the catalog's time zone (they used to be read in the machine's own offset for `DateTimeOffset`).

## 0.6 → 0.7

### A parameter `{ "$path": ... }` is a path

An action parameter whose value is an object with a `$path` key is now a path, read when the action runs
(`"to": { "$path": "@customer.Email" }`). `$path` with other keys, or inside a parameter's value, is rejected when
the rule is parsed. Rules are affected only if they pass such an object as a literal value; there's no escape for it
yet, so pass it under another key. Other parameters are unchanged, and actions that resolve path strings themselves
keep working.

### `FieldDefinition` has `IsRequired`

`FieldDefinition` gained `IsRequired` (the field is listed in its object's `required`), a positional parameter after
`IsNullable`. Code that creates or deconstructs a `FieldDefinition` passes or receives it there; code that reads its
properties is unaffected.

### Optional: typed actions, path parameters and `MapRuleLoom`

Nothing else has to change, but an action that took a path as text and resolved it can now let RuleLoom do it. Before:

```csharp
public sealed class SendEmailAction : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken ct = default)
    {
        var to = context.Resolve(action.GetParameter<string>("to")) as string;   // "to": "Customer.Email"
        var subject = action.GetParameter<string>("subject");
        // ...
    }
}
```

After: a typed action, and rules that mark the path:

```csharp
public sealed record SendEmailParameters(string To, string Subject);

public sealed class SendEmailAction : IAction<SendEmailParameters>
{
    public Task<ActionResult> ExecuteAsync(SendEmailParameters parameters, ActionDefinition action, EvaluationContext context,
        CancellationToken ct = default)
    {
        // parameters.To is the address: RuleLoom resolved { "$path": "Customer.Email" }
    }
}
```

```json
{ "action": "send-email", "parameters": { "to": { "$path": "Customer.Email" }, "subject": "Your order has shipped" } }
```

Change the action and its rules together: a typed action reads `"to": "Customer.Email"` as the text itself. What you
get:

- the catalog document describes the parameters (label them with `ActionDescriptor<TAction, TParameters>`), and
  `RuleValidator` reports unknown, missing and wrongly typed parameters and checks the paths;
- paths can read contexts (`{ "$path": "@customer.Email" }`), loaded with the rule's other contexts.

If you run scheduled actions yourself, create their `EvaluationContext` with the event type, the catalog and an
`IContextProviderFactory` (as the engine does), so paths into contexts resolve when the action runs; with
`new EvaluationContext(current, previous)` they fail as undeclared.

An endpoint you wrote to serve `catalog.ToJson()` can be replaced by the `RuleLoom.AspNetCore` package:
`app.MapRuleLoom("/rules")` maps `GET /rules/catalog` and `POST /rules/validate`.

## 0.5 → 0.6

### The engine, the validator and `AddRuleLoom` run on a catalog

What rules can use (event schemas, context providers, operators and actions) now lives in one `Catalog`, built once
with `CatalogBuilder` and fixed afterwards. Names are looked up in the catalog; factories only create instances from
the catalog's registrations.

Without DI:

```csharp
// 0.5
var engine = new RuleEngine(
    new ConditionEvaluator(BuiltInOperators.Create()),
    new DirectActionFactory().Add(new SendEmailAction(mailer), "notify"));
var validator = new RuleValidator(BuiltInOperators.Create(), actionFactory);

// 0.6
var catalog = new CatalogBuilder()                 // starts with the built-in operators
    .AddAction<SendEmailAction>("notify")
    .Build();
var engine = new RuleEngine(catalog, new DirectActionFactory().Add(new SendEmailAction(mailer)));
var validator = new RuleValidator(catalog);
```

With DI, the registration methods move into `AddCatalog`:

```csharp
// 0.5
services.AddRuleLoom(rules => rules.AddAction<SendEmailAction>().AddOperator<ContainsOperator>());

// 0.6
services.AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
    .AddAction<SendEmailAction>()
    .AddOperator<ContainsOperator>()));
```

| 0.5 | 0.6 |
|---|---|
| `new RuleEngine(evaluator, actionFactory, timeProvider)` | `new RuleEngine(catalog, actions?, contextProviders?, timeProvider?)` |
| `new ConditionEvaluator(operators)` | `new ConditionEvaluator(catalog)`; the engine builds its own |
| `new RuleValidator(operators, actionFactory)` | `new RuleValidator(catalog)` |
| `IActionFactory.TryGetAction(name, out action)`, `ActionNames` | `IActionFactory.Create(ActionRegistration)`; names are in `catalog.Actions` / `catalog.TryGetAction(name, ...)` |
| `DirectActionFactory.Add(action, name?)` | `DirectActionFactory.Add(action)`: an instance per type; types with a parameterless constructor need none. The name goes on `CatalogBuilder.AddAction<T>(name)` |
| `RuleLoomBuilder.AddAction`/`AddOperator`/`RemoveOperator`/`RemoveBuiltInOperators` | the same methods on `CatalogBuilder`, inside `AddCatalog(...)` |
| `IConditionEvaluator` registered by `AddRuleLoom` | not registered; resolve `RuleEngine` (or `Catalog`) |
| Operators created by DI (`AddOperator<T>()` with constructor dependencies) | operators need a parameterless constructor, or add an instance: `AddOperator(new MyOperator(...))` |

Other changes:

- **The catalog is built when the host starts.** A broken catalog (two actions with the same name, an event declaring a
  context without a provider) stops the app from starting. In an app without a host, call
  `await serviceProvider.StartRuleLoomAsync()` once at startup; resolving `RuleEngine` before that throws.
- **Actions are no longer registered as services by `AddRuleLoom`.** They're created in each scope with their
  dependencies, or resolved as registered when you register the type yourself. Register disposable actions yourself,
  so the scope disposes them.
- **Rules on an event type that isn't in the catalog are invalid.** `RuleValidator.Validate` reports
  `Event type 'X' isn't in the catalog.` at `$.eventType`. The engine still runs them. Add an `EventSchema<T>` for
  each event you validate rules for (`AddEvent<T>()` or `AddFromAssemblyOf<T>()`).

What to change: build a catalog (or move registrations into `AddCatalog`), pass it to the engine and the validator,
and add event schemas for the events your rules are validated against.

### Paths starting with `@` or `#` are no longer property names

`@name` now reads a [context](json-rules.md#contexts) (related data loaded by a provider), and `#` is reserved for
functions. Before, `"@type"` or `"#tag"` at the start of a path read a property with that name.

- `@name...` at the start of a path, on an event whose schema doesn't declare the context `name`, now fails the rule.
- Paths starting with `#`, and `@` right after `$current.`/`$previous.`, are rejected when the rule is parsed.
- `@` and `#` later in a path (`Payload.@type`) still read properties.

What to change: rules whose first path segment is a property starting with `@` or `#` must reach it from a parent
property, or the property must be renamed.

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

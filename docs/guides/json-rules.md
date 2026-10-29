# JSON Rule Parser Documentation


## Overview

The `RuleJsonParser` is a performant, type-safe JSON parser for converting JSON rule definitions into strongly-typed `RuleDefinition` objects. It uses `System.Text.Json` with custom polymorphic converters to efficiently handle the tree-based condition structure.

## Features

- **Performant**: Uses System.Text.Json for fast, low-allocation parsing
- **Type-Safe**: Automatically detects and deserializes the correct ConditionNode types
- **Polymorphic**: Handles `PathOperand`, `ValueOperand`, and `OperatorCondition` transparently
- **Case-Insensitive**: JSON property names (`EventType` or `eventType`) and the names inside a rule — operator
  names (`"EQ"`), action names (`"Send-Email"`), event types and property paths (`"price"` finds `Price`) — are
  matched ignoring case. An exact-case property match wins if a type has properties differing only in case.
  String *values* still compare case-sensitively.
- **Streaming Support**: Supports both string and stream-based parsing for large rule sets
- **Batch Parsing**: Parse multiple rules from a single JSON array

## JSON Structure

### Basic Rule Format

```json
{
  "id": "rule-id",
  "name": "Rule Name",
  "eventType": "EventName",
  "priority": 1,
  "condition": { ... },
  "actions": [ ... ]
}
```

`id`, `name`, `eventType` and `condition` are required. The `id` identifies the rule in results and scheduled
actions, so keep it stable across loads (e.g. the rule's key in your store).

### Condition Types

#### 1. PathOperand - Reference a property path

```json
{ "path": "Price" }
{ "path": "User.Address.ZipCode" }  // Nested paths supported
```

#### 2. ValueOperand - Literal value

```json
{ "value": 100 }
{ "value": "text" }
{ "value": true }
{ "value": null }
{ "value": [1, 2, 3] }
{ "value": { "nested": "object" } }
```

#### 3. OperatorCondition - Operator with operands

```json
{
  "operator": "eq",
  "operands": [
    { "path": "Price" },
    { "value": 100 }
  ]
}
```

## Usage Examples

### Example 1: Parse a single rule

```csharp
var json = """
{
  "id": "high-price-alert",
  "name": "High Price Alert",
  "eventType": "ProductUpdated",
  "priority": 1,
  "condition": {
    "operator": "changed",
    "operands": [
      { "path": "Price" }
    ]
  },
  "actions": [
    {
      "action": "console-log",
      "parameters": { "message": "Price changed!" }
    }
  ]
}
""";

var rule = RuleJsonParser.Parse(json);
Console.WriteLine($"Rule: {rule.Name}");
```

### Example 2: Complex nested conditions

```csharp
var json = """
{
  "id": "complex-rule",
  "name": "Complex Rule",
  "eventType": "ProductUpdated",
  "condition": {
    "operator": "and",
    "operands": [
      {
        "operator": "changed",
        "operands": [{ "path": "Price" }]
      },
      {
        "operator": "not",
        "operands": [
          {
            "operator": "eq",
            "operands": [
              { "path": "Price" },
              { "value": 999999 }
            ]
          }
        ]
      }
    ]
  },
  "actions": []
}
""";

var rule = RuleJsonParser.Parse(json);
```

### Example 3: Parse from stream (async)

```csharp
using var stream = File.OpenRead("rules.json");
var rule = await RuleJsonParser.ParseAsync(stream);
```

### Example 4: Parse multiple rules

```csharp
var json = """
[
  { "id": "rule-1", "name": "Rule 1", "eventType": "Event1", "condition": { "operator": "changed", "operands": [{ "path": "Field1" }] } },
  { "id": "rule-2", "name": "Rule 2", "eventType": "Event2", "condition": { "operator": "changed", "operands": [{ "path": "Field2" }] } }
]
""";

var rules = RuleJsonParser.ParseMany(json);
foreach (var rule in rules)
{
    Console.WriteLine($"- {rule.Name}");
}
```

`ParseMany` is all or nothing: one malformed rule rejects the array. To load what you can from a file and report the
rest, parse it rule by rule with `ParseEach`, which returns one `RuleParseResult` per rule, in order:

```csharp
foreach (var result in RuleJsonParser.ParseEach(json))
{
    if (result.IsParsed)
        rules.Add(result.Rule);
    else
        Console.WriteLine(result.Error.Message);   // "'condition' is required. (at $[1].condition)"
}
```

Malformed JSON, or JSON that isn't an array, still throws: there are no rules to tell apart.

## Performance Considerations

### Optimizations

1. **Stateless Converter**: The `ConditionNodeJsonConverter` is stateless and reusable across multiple parse operations
2. **Streaming**: Use `ParseAsync(stream)` for large files to avoid loading entire JSON into memory
3. **Lazy Parsing**: JSON is only parsed when accessed, minimizing unnecessary processing
4. **Pooled Allocations**: System.Text.Json uses buffer pooling internally

### Benchmarks (Estimated)

- Single simple rule: ~0.1-0.2 ms
- Complex rule with 10+ nested conditions: ~0.5-1 ms
- Batch parse 100 rules: ~50-100 ms

For actual benchmarks, run BenchmarkDotNet tests against your use cases.

## Error Handling

Malformed JSON throws `JsonException`. Well-formed JSON that doesn't describe a valid rule throws
`RuleParseException` (a `JsonException`), whose `Path` is the JSONPath of the problem, e.g. `$.condition.operands[1]`,
or `$[2].condition` for the third rule passed to `ParseMany` (`ParseEach` returns it in that rule's result instead):

```csharp
try
{
    var rule = RuleJsonParser.Parse(json);
}
catch (RuleParseException ex)
{
    Console.WriteLine($"Invalid rule at {ex.Path}: {ex.Message}");
}
catch (JsonException ex)
{
    Console.WriteLine($"Malformed JSON: {ex.Message}");
}
```

A rule is rejected when:
- `id`, `name` or `eventType` is missing, empty or not a string, or `condition` is missing
- the root condition (or an action's condition) is not an operator — wrap paths and values in one, e.g.
  `{ "operator": "eq", "operands": [{ "path": "Active" }, { "value": true }] }`
- a condition node doesn't have exactly one of `operator`, `path` or `value` (an empty `{}` is not allowed; use
  `{ "value": null }`), or its `operator`/`path` is empty
- `operands` or `actions` isn't an array, an action has no `type`, or `parameters` isn't an object
- a `$path` parameter has other keys, isn't a valid path, or sits inside a parameter's value
- a value has the wrong type for its property, e.g. `"priority": "high"` or a schedule `"mode": "Weekly"`

Whether an operator exists or has the right number of operands isn't checked by the parser. To catch those before a
rule is saved or run, validate it against the catalog the engine runs on:

```csharp
var validator = new RuleValidator(catalog);   // or resolve RuleValidator from DI
var result = validator.Validate(rule);

foreach (var error in result.Errors)
    Console.WriteLine($"{error.Path}: {error.Message}");   // "$.condition.operands[0].path: 'Totl' is not a field of OrderPlaced."
foreach (var warning in result.Warnings)
    Console.WriteLine($"{warning.Path}: {warning.Message}");
```

`RuleValidator` reports every problem at once: an event type that isn't in the catalog, unknown operators, too few or
too many operands, unknown actions, and `Delay`/`Advance` schedules without `amount` or `unit`. Every path in
conditions, `$path` parameters and schedule `referencePath`s is checked against the event's schema:

- **Errors:** unknown fields (ignored fields count as unknown), paths below a value without fields or into a list
  (`Lines.Sku`, `@watchers.Email`), contexts the event doesn't declare, contexts whose provider isn't registered
  (possible only for event schemas that came from a catalog store), `$current.@x`/`$previous.@x`, and `changed` on a
  context. `$previous.` paths are checked against the subject.
- **Warnings** (the rule stays valid; `IsValid` only looks at errors): `eq`, `gt`, `gte`, `lt` and `lte` comparing types
  that can never match (a number field with `"100"`, a boolean with `"yes"`, a date with text that isn't ISO 8601,
  `gt` on a list or object), and paths into a context whose provider has no schema (an untyped provider), which can't
  be checked. Enum fields aren't checked.
- Paths below untyped fields (`object`, JSON), dictionaries and repeated types aren't checked.

A typed action's parameters (`IAction<TParameters>`) are checked against its parameters schema; untyped actions'
parameters aren't:

- **Errors:** a parameter the type doesn't have (`$.actions[0].parameters.subjet`), a required one that's missing
  (reported at `$.actions[0].parameters`), and a value of the wrong type: text, whole numbers, numbers, booleans, ISO
  8601 dates, enum names (ignoring case), objects and lists, checked inside too (`$.actions[0].parameters.cc.email`,
  `…tags[1]`); `null` only where the parameter is nullable. Names ignore case, as when the action reads them.
- **Warnings:** a `$path` whose field the action can't read as the parameter's type (`"attempts": { "$path": "Email" }`
  for a number), reported at `…parameters.attempts.$path`. JSON state may still convert, so it doesn't block the rule.
- Rules on an event type that isn't in the catalog get an error at `$.eventType`, and their paths aren't checked.

## Type Inference Rules

The converter uses these rules to determine ConditionNode type:

1. If `"operator"` property exists → `OperatorCondition`
2. Else if `"path"` property exists → `PathOperand`
3. Else if `"value"` property exists → `ValueOperand`

A node with none or more than one of these properties is rejected with a `RuleParseException` (see Error Handling).

## Paths and State

A `path` names a property, with `.` for nested properties (`"Address.City"`). Names ignore case. A path reads the
**current** state unless it starts with a variable:

| Path | Reads |
|---|---|
| `"Price"`, `"Address.City"` | the current state |
| `"$current.Price"` | the current state (explicit) |
| `"$previous.Price"` | the previous state (`null` if there is none) |
| `"@customer.Tier"` | a [context](#contexts): related data loaded by a provider you register |
| `"$event.OccurredAt"`, `"$event.Metadata.region"` | the event's [envelope](#the-event-envelope) |

Other names starting with `$` are reserved and rejected, and so are paths starting with `#` (reserved for functions). Without the `$`, `current` and `previous` are ordinary
property names, so a property called `Previous` is simply `"Previous.X"`. `changed` compares current with previous
whichever way its path is written: `Price`, `$current.Price` and `$previous.Price` are equivalent.

Paths follow the **runtime** value: a property declared as `object`, an interface or a base type is read through
whatever it holds, including JSON.

Custom operators read paths with `RulePath` rather than taking the string apart:
`RulePath.Parse(ctx.GetPath(0))` gives its `Root` (`Current`, `Previous`, `Context` or `Event`), the `Context` name and the
`Property`; `path.InState(PathRoot.Previous)` is the same property in the previous state, and `ctx.Resolve(path)` reads
it. Invalid paths (`#…`, a bare `@`, unknown `$` variables, `$previous.@x`) are rejected by the parser, by
`RuleValidator` and, in rules built in code, by the engine, which fails the rule.

A custom operator can also check its operands when rules are validated, by implementing `ValidateOperands`. The
validator calls it with a valid operand count and adds the locations:

```csharp
public void ValidateOperands(OperandValidationContext context)
{
    if (context.GetPath(0) is null)
        context.Error(0, "'between' expects a path first.");
    if (context.GetField(0) is { Type: not (FieldType.Integer or FieldType.Number) })   // with schemas
        context.Warning("'between' compares numbers.");
}
```

## The Event Envelope

Besides its state, an event can carry what the app knows about it: an id, its subject, its source, when it happened
and app-defined metadata (e.g. a tenant id). Run rules on a `RuleEvent` to pass them:

```csharp
var placed = new RuleEvent("OrderPlaced", order)
{
    EventId = message.Id,
    Subject = "order-1001",
    OccurredAt = message.Timestamp,
    Metadata = new Dictionary<string, object?> { ["tenantId"] = "acme-shop", ["region"] = "EU" }
};

var result = await engine.ExecuteAsync(placed, rules);
```

Rules read the envelope with `$event.` paths: `$event.EventType`, `$event.EventId`, `$event.Subject`, `$event.Source`,
`$event.OccurredAt` (e.g. as a schedule's `referencePath`) and `$event.Metadata.<key>` (keys ignore case). Actions and
operators get it from `context.Event`, and context providers from `request.Event`. The envelope has no previous state,
so `$previous.$event…` is invalid and `changed` can't be used on it. `RuleValidator` checks the envelope's fields; it
can't check metadata keys, which the app defines.

`ExecuteAsync(eventType, current, previous, rules)` is the same as an event without envelope data. `ToJson()` and
`RuleEvent.Parse(json)` write and read an event (for a queue or a job store), with the previous state as `previous` or,
when it was built from changes, as `changes`; `occurredAt` must state its offset. Deduplicating by `EventId` is up to
your app or transport.

## Contexts

A rule often needs data beyond the event's subject: the order's customer, the warehouse, the people watching an ad.
Instead of sending it with every event, the event's schema declares the related data it has (its **contexts**) and a
**context provider** in the catalog loads it. Rules use it with `@name`:

```json
{ "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "Gold" }] }
```

```csharp
public sealed class CustomerProvider(ICustomersClient customers) : ContextProvider<CustomerDto>
{
    protected override async ValueTask<CustomerDto?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        request.Resolve("CustomerId") is string id ? await customers.FindAsync(id, cancellationToken) : null;
}

public sealed class OrderPlacedSchema : EventSchema<OrderDto>
{
    public OrderPlacedSchema()
    {
        Event("OrderPlaced");
        Context("customer");   // loaded by the provider registered as "customer"
    }
}

services.AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
    .AddEvent<OrderPlacedSchema>()
    .AddContextProvider<CustomerProvider>("customer")));   // created in each scope
// without DI: new RuleEngine(catalog, actions, new DirectContextProviderFactory().Add(new CustomerProvider(customers)))
```

- **Only declared contexts.** A rule can use the contexts its event declares, and nothing else: `@warehouse` on an
  event that doesn't declare it (or on an event type that isn't in the catalog) fails the rule, even when a provider is
  registered under that name. Building the catalog fails when an event declares a context with no provider.

- **The provider decides how to find the data.** It gets the context name and the event (type, current and previous
  state; `request.Resolve(path)` reads the subject), and returns a .NET object, JSON, a collection, or `null` when there
  is none. `ContextProvider<T>` (one entity) and `CollectionContextProvider<TItem>` (a collection) are typed base
  classes; implement `IContextProvider` directly to return untyped data such as JSON from another service.
- **References from event schemas.** When the event's schema declares how it refers to a context
  (`Context("toAccount").ReferencedBy(t => t.ToAccountId)`, see `RuleLoom.Schemas`), the engine resolves the reference
  from the event and passes it as `request.Reference`; read it with
  `request.GetReference<string>()` (a single value) or `GetReference<ZoneKey>()` (a composite reference mapped onto a
  record by part name). Without a declared reference, `Reference` is `null` and the provider reads the subject itself.
- **Describing a context's fields.** A typed provider's fields come from the type it loads, and a
  `ContextSchema<T>` of that type (in the provider's or the type's assembly) adds `Label("Account")`,
  `Field(a => a.Balance).Label("Balance")` or `.Ignore()` in its constructor. The schema is kept in the catalog under
  the provider's registered name; it's used to validate `@` paths and to show rule authors what they can pick. Untyped
  providers (`IContextProvider`, or a JSON or `object` type) have no schema, unless a catalog store holds one.
- **One provider, several contexts.** A schema can say which provider loads a context:
  `Context("fromAccount").LoadedBy("account").ReferencedBy(t => t.FromAccountId)` and
  `Context("toAccount").LoadedBy("account").ReferencedBy(t => t.ToAccountId)`, with one
  `AddContextProvider<AccountProvider>("account")`. Rules still use the context names (`@fromAccount.balance`); the
  provider gets the context name in `request.Name`. Without `LoadedBy`, the provider registered under the context's name
  loads it.
- **Loaded only when used, once per event.** Before evaluating, the engine loads the contexts that the event's rules
  use (in conditions, action conditions, schedule `referencePath`s and `$path` parameters), in parallel. Contexts no rule uses aren't loaded.
- **Actions** can load any context the event declares on demand, e.g. a collection to email:
  `var watchers = await context.GetContextAsync<WatcherDto[]>("watchers", ct);` (JSON is deserialized; the untyped
  `GetContextAsync(name)` returns the value as loaded). It shares the same per-event cache.
- **Failures stay local.** If a provider throws, only the rules (or actions) that use that context fail, with a
  `ContextLoadException`; other rules run.
- **No previous state.** Contexts are read as they are now: `$previous.@customer` is rejected and `changed` on a context
  fails the rule.
- Names ignore case. A collection comes back as is; paths into it (`@watchers.Email`) resolve to `null`.

## Rules on JSON State

The state doesn't have to be a .NET class. Rules run on JSON as System.Text.Json gives it to you without a class:
a `JsonElement` (from `JsonDocument`, `JsonSerializer.Deserialize<object>`, or an ASP.NET Core body bound to
`object`/`JsonElement`) or a `JsonNode` (`JsonNode.Parse`, `JsonObject`). JSON can also sit inside a .NET object,
e.g. an envelope with a `JsonElement Payload` or `object Payload` property (`"Payload.Customer.Name"`).

```csharp
using var document = JsonDocument.Parse(requestBody);
var result = await engine.ExecuteAsync("OrderUpdated", document.RootElement, previousElement, rules);
```

**Keys** match like property names: an exact-case key wins; otherwise a single case-insensitive match is used. If
several keys match ignoring case (`"price"` and `"Price"` for `PRICE`), or the same key appears twice, the path
resolves to `null`.

**Values** are turned into plain .NET values, so every operator, action parameter and schedule works as usual:

| JSON | Resolves to |
|---|---|
| String | `string`; ISO 8601 date strings compare as dates (see [Comparing Values](#comparing-values)) and work as a schedule `referencePath` |
| `true` / `false` | `bool` (usable directly as a condition) |
| Integral number (`42`) | `long` |
| Other number (`19.99`, `1.0`, `1e2`) | `decimal` (exact), or `double` beyond `decimal`'s range; `null` beyond `double`'s |
| `null`, missing key | `null` |
| Object, array | the JSON itself (`JsonElement` or `JsonNode`) |

A `JsonValue` created from a .NET value (`JsonValue.Create(19.99m)`) returns that value unchanged.

**Limits:**
- Arrays can't be indexed (`Lines.0` and `Lines[0]` resolve to `null`).
- JSON has no enum type: `"Shipped"` compares as a string (case-sensitive), not as an enum. Dates work through ISO 8601
  strings.
- Keep the `JsonDocument` alive until `ExecuteAsync` returns. A `JsonElement` from a disposed document throws
  `ObjectDisposedException` instead of resolving to `null`.

### Sending only the changed fields

`changed` and `$previous` need the previous state, but a producer doesn't have to send a second full copy. Send the
current state plus the changed fields with their **old** values, nested like the state, and wrap them in a
`PreviousState`:

```json
{
  "current": { "Status": "Paid", "Total": 640.5, "Customer": { "Email": "ada@example.com" } },
  "changes": { "Status": "AwaitingPayment" }
}
```

```csharp
var previous = PreviousState.FromChanges(request.Current, request.Changes);
var result = await engine.ExecuteAsync("OrderUpdated", request.Current, previous, rules);
```

Rules behave as if the full previous state had been sent: `$previous.Status` is `"AwaitingPayment"`,
`$previous.Total` comes from the current state, `changed Status` is true and `changed Total` is false.

- `changes` must be a JSON object (`JsonElement` or `JsonObject`); `current` can be JSON or a .NET object.
- An explicit `null` means the field was null; a missing key means it didn't change.
- A changed parent covers everything below it: with `"Customer": null`, `$previous.Customer.Email` is `null`.
- A path that ends at an object listed in `changes` (`changed Customer`) sees that partial object, so `changed`
  reports it as changed; don't compare such objects with `eq`.
- `$previous` alone returns the `PreviousState` (with `Current` and `Changes`), not a merged copy.

## Comparing Values

JSON values are parsed as `int`, `long`, `double`, `string`, `bool` or `null`, while domain properties are often
`decimal`, enums or dates. `eq` compares them with `ValueComparer` (namespace `RuleLoom.Abstractions.Values`),
so a value matches when it means the same thing:

| Property type | JSON value | Matches when |
|---|---|---|
| Any number (`decimal`, `int`, `double`, …) | Number | Equal by value: `100` matches `100m`, `5.0` matches `5` |
| Enum | String | Same name, ignoring case: `"active"` matches `Status.Active`; `[Flags]` combinations like `"Read, Write"` work |
| Enum | Number | Same underlying value: `1` matches `Status.Active` (= 1) |
| `DateTime` / `DateTimeOffset` | String | Same instant, given as **ISO 8601**: `"2026-05-01T12:00:00Z"`, `"2026-05-01T14:00:00+02:00"`; no offset means UTC; `"2026-05-01"` means UTC midnight |
| `string` (ISO 8601 date) | ISO 8601 string | Same instant: `"2026-05-01T14:00:00+02:00"` matches `"2026-05-01T12:00:00Z"`; `"2026-05-01"` matches `"2026-05-01T00:00:00Z"`. Typical for JSON state |
| `string` (ISO 8601 date) | Other string | Never matches, and can't be ordered |
| `string` | String | Exactly equal (case-sensitive) |
| JSON object or array | JSON object or array | Same content, ignoring property order (`changed` on an object path compares content) |

Values that don't fit these rules never match: e.g. a non-ISO date string (`"05/01/2026"`), a number as text
(`"100"` vs `100`), or `NaN`. `changed` uses the same equality, so `100` → `100.0` or the same instant in another
offset isn't a change.

`gt`, `gte`, `lt` and `lte` order values with the same rules: numbers by value, enums by underlying number (so
`"Active"` is compared as `Status.Active`'s value), dates as instants, and strings ordinally (`"B"` < `"a"`). When two
values can't be ordered — a `null`, a number against a non-numeric string, or the cases above — all four evaluate
to `false`, so `gt` and `lte` aren't opposites for such values. Custom operators can call `ValueComparer.AreEqual` / `ValueComparer.TryCompare`
to follow the same rules.

## Action Parameters

An action's `parameters` are parsed into plain .NET values, the same way as condition values: strings, `int`/`long`/
`double` numbers, booleans, `null`, lists (`List<object?>`) and objects (`Dictionary<string, object?>`).

A parameter can also be a **path**, read when the action runs: an object whose only key is `$path`.

```json
{ "action": "send-email",
  "parameters": { "to": { "$path": "@customer.Email" }, "total": { "$path": "Total" }, "subject": "Thanks for your order" } }
```

- Any other JSON is a value, including objects with a `path` key (`{ "path": "/orders" }`) and text starting with `$`.
- `$path` with other keys, a `$path` that isn't a valid path, and a `$path` inside a parameter's value (in an object
  or a list) are rejected with a `RuleParseException`. A path is always a parameter's whole value.
- Paths read the event's current or previous state or a context, like condition paths. The engine loads the contexts
  they use with the rest; a scheduled action run later loads them when it reads its parameters.

Typed actions (`IAction<TParameters>`) receive the parameters as an object with the paths resolved. Other actions read
them with `GetParametersAsync<T>(context)`, or one at a time with `GetParameterAsync<T>(name, context)` (resolving a path)
and `GetParameter<T>(name)` (values only), which convert with the [comparison rules](#comparing-values):

```csharp
var amount = action.GetParameter<decimal>("amount");                    // JSON 250 → 250m
var sendAt = action.GetParameter<DateTimeOffset>("sendAt");             // "2026-05-01T09:00:00Z"
var urgent = action.TryGetParameter<bool>("urgent", out var flag) && flag;
var to = await action.GetParameterAsync<string>("to", context, ct);     // { "$path": "@customer.Email" }
```

A missing parameter throws `KeyNotFoundException`, and a value that can't be converted (e.g. `"100"` as `int`) throws
`InvalidOperationException`, as does `GetParameter<T>` on a path. Names match exactly, or ignoring case when that's
unambiguous.

## Scheduling Actions

An action with a `schedule` isn't run by the engine: it comes back with status `Scheduled` and a `ScheduledFor` time
(UTC), and `result.ScheduledActions` lists it with its definition so your scheduler can dispatch it.

```json
{ "action": "send-email", "schedule": { "mode": "Advance", "amount": 2, "unit": "Hours", "referencePath": "DueAt" } }
```

| Field | Values |
|---|---|
| `mode` | `Immediate` (default, runs now), `Delay` (reference + amount), `Advance` (reference − amount) |
| `amount` | Whole number; required for `Delay` / `Advance` |
| `unit` | `Seconds`, `Minutes`, `Hours`, `Days`; required for `Delay` / `Advance` |
| `referencePath` | Optional path to a `DateTime`, `DateTimeOffset` or ISO 8601 string on the object; without it, the reference is "now" |

Mode and unit names ignore case. A reference that's missing, `null` or not a date makes the action `Failed`.

## Integration with RuleEngine

```csharp
// Parse from JSON
var rule = RuleJsonParser.Parse(jsonString);

// Use with RuleEngine
var catalog = new CatalogBuilder().AddAction<ConsoleLogAction>().Build();   // with the built-in operators

var engine = new RuleEngine(catalog);
RuleEngineResult result = await engine.ExecuteAsync(eventType, current, previous, [rule]);
```

## Best Practices

1. **Validate JSON schema** before parsing in production
2. **Use streams** for large JSON files (>10 MB)
3. **Cache parsed rules** if they're loaded frequently
4. **Handle exceptions** appropriately in your application
5. **Use camelCase** property names in JSON for consistency with .NET conventions
6. **Document operator names** and operand counts for your custom operators

## Custom Operators

The parser doesn't validate operator names or operand counts; `RuleValidator` does (see Error Handling), and the engine reports an unknown operator as a `Failed` rule when it runs. Register custom operators with the `ConditionEvaluator` (and the `RuleValidator`) before executing rules.

```csharp
// This will parse successfully but fail at runtime if "custom_op" isn't registered
var json = """
{
  "id": "custom-op-rule",
  "name": "Custom Op Rule",
  "eventType": "Event",
  "condition": { "operator": "custom_op", "operands": [] },
  "actions": []
}
""";
var rule = RuleJsonParser.Parse(json);
// RuleValidator reports "$.condition.operator: Unknown operator 'custom_op'."
// If it runs anyway, the rule's result has Status == Failed with the error.
```


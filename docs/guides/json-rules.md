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
or `$[2].condition` for the third rule passed to `ParseMany`:

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

Whether an operator exists or has the right number of operands isn't checked by the parser. To catch those before a
rule is saved or run, validate it against the operators and actions you've registered:

```csharp
var validator = new RuleValidator(operators, actionFactory);   // same ones as for the engine
var result = validator.Validate(rule);

foreach (var error in result.Errors)
    Console.WriteLine($"{error.Path}: {error.Message}");   // e.g. "$.condition.operands[1].operator: Unknown operator 'gt'."
```

`RuleValidator` reports every problem at once: unknown operators, too few or too many operands, unknown actions,
and `Delay`/`Advance` schedules without `amount` or `unit`. It doesn't check property paths.

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

Other names starting with `$` are reserved and rejected. Without the `$`, `current` and `previous` are ordinary
property names, so a property called `Previous` is simply `"Previous.X"`. `changed` compares current with previous
whichever way its path is written: `Price`, `$current.Price` and `$previous.Price` are equivalent.

Paths follow the **runtime** value: a property declared as `object`, an interface or a base type is read through
whatever it holds, including JSON.

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

## Comparing Values

JSON values are parsed as `int`, `long`, `double`, `string`, `bool` or `null`, while domain properties are often
`decimal`, enums or dates. `eq` compares them with `ValueComparer` (namespace `RuleForge.Abstractions.Values`),
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
`double` numbers, booleans, `null`, lists (`List<object?>`) and objects (`Dictionary<string, object?>`). Actions read
them with `GetParameter<T>`, which converts with the [comparison rules](#comparing-values):

```csharp
var amount = action.GetParameter<decimal>("amount");          // JSON 250 → 250m
var sendAt = action.GetParameter<DateTimeOffset>("sendAt");   // "2026-05-01T09:00:00Z"
var urgent = action.TryGetParameter<bool>("urgent", out var flag) && flag;
```

A missing parameter throws `KeyNotFoundException`, and a value that can't be converted (e.g. `"100"` as `int`) throws
`InvalidOperationException`. Names match exactly, or ignoring case when that's unambiguous.

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
IConditionOperator[] operators =
[
    new EqualsOperator(), new ChangedOperator(), new AndOperator(), new OrOperator(), new NotOperator(),
    new GreaterThanOperator(), new GreaterThanOrEqualOperator(), new LessThanOperator(), new LessThanOrEqualOperator()
];
IAction[] actions = [new ConsoleLogAction()];

var engine = new RuleEngine(new ConditionEvaluator(operators), new DirectActionFactory(actions));
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


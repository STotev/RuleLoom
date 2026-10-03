# JSON Rule Parser Documentation

> **Note**: action `schedule` blocks (`mode`, `amount`, `unit`, `referencePath`) are not documented here yet —
> see [the scheduling spec](../specs/2026-03-28-scheduling-model-abstractions-design.md) until this guide is refreshed in #6.

## Overview

The `RuleJsonParser` is a performant, type-safe JSON parser for converting JSON rule definitions into strongly-typed `RuleDefinition` objects. It uses `System.Text.Json` with custom polymorphic converters to efficiently handle the tree-based condition structure.

## Features

- **Performant**: Uses System.Text.Json for fast, low-allocation parsing
- **Type-Safe**: Automatically detects and deserializes the correct ConditionNode types
- **Polymorphic**: Handles `PathOperand`, `ValueOperand`, and `OperatorCondition` transparently
- **Case-Insensitive**: Property names are matched case-insensitively (e.g., `EventType` or `eventType`)
- **Streaming Support**: Supports both string and stream-based parsing for large rule sets
- **Batch Parsing**: Parse multiple rules from a single JSON array

## JSON Structure

### Basic Rule Format

```json
{
  "name": "Rule Name",
  "eventType": "EventName",
  "priority": 1,
  "condition": { ... },
  "actions": [ ... ]
}
```

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
  "name": "High Price Alert",
  "eventType": "PropertyUpdated",
  "priority": 1,
  "condition": {
    "operator": "changed",
    "operands": [
      { "path": "Price" }
    ]
  },
  "actions": [
    {
      "type": "log",
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
  "name": "Complex Rule",
  "eventType": "PropertyUpdated",
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
  { "name": "Rule 1", "eventType": "Event1", "condition": { "operator": "changed", "operands": [{ "path": "Field1" }] } },
  { "name": "Rule 2", "eventType": "Event2", "condition": { "operator": "changed", "operands": [{ "path": "Field2" }] } }
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
- `name` or `eventType` is missing, empty or not a string, or `condition` is missing
- the root condition (or an action's condition) is not an operator — wrap paths and values in one, e.g.
  `{ "operator": "eq", "operands": [{ "path": "Active" }, { "value": true }] }`
- a condition node doesn't have exactly one of `operator`, `path` or `value` (an empty `{}` is not allowed; use
  `{ "value": null }`), or its `operator`/`path` is empty
- `operands` or `actions` isn't an array, an action has no `type`, or `parameters` isn't an object

Whether an operator exists or has the right number of operands is checked when the rule runs, not when it is parsed.

## Type Inference Rules

The converter uses these rules to determine ConditionNode type:

1. If `"operator"` property exists → `OperatorCondition`
2. Else if `"path"` property exists → `PathOperand`
3. Else if `"value"` property exists → `ValueOperand`

A node with none or more than one of these properties is rejected with a `RuleParseException` (see Error Handling).

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
| `string` | String | Exactly equal (case-sensitive) |

Values that don't fit these rules never match: e.g. a non-ISO date string (`"05/01/2026"`), a number as text
(`"100"` vs `100`), or `NaN`. Custom operators can call `ValueComparer.AreEqual` / `ValueComparer.TryCompare`
to follow the same rules.

## Integration with RuleEngine

```csharp
// Parse from JSON
var rule = RuleJsonParser.Parse(jsonString);

// Use with RuleEngine
IConditionOperator[] operators =
[
    new EqualsOperator(), new ChangedOperator(), new AndOperator(), new OrOperator(), new NotOperator()
];
IActionExecutor[] actions = [new LogAction()];

var engine = new RuleEngine(new ConditionEvaluator(operators), new DirectActionExecutorFactory(actions));
IReadOnlyList<RuleExecutionResult> results = await engine.ExecuteAsync(eventType, current, previous, [rule]);
```

## Best Practices

1. **Validate JSON schema** before parsing in production
2. **Use streams** for large JSON files (>10 MB)
3. **Cache parsed rules** if they're loaded frequently
4. **Handle exceptions** appropriately in your application
5. **Use camelCase** property names in JSON for consistency with .NET conventions
6. **Document operator names** and operand counts for your custom operators

## Custom Operators

The parser doesn't validate operator names or operand counts. Validation occurs during rule evaluation. Ensure operators are registered in your `IConditionEvaluator` before executing rules.

```csharp
// This will parse successfully but fail at runtime if "custom_op" isn't registered
var json = """
{
  "name": "Custom Op Rule",
  "eventType": "Event",
  "condition": { "operator": "custom_op", "operands": [] },
  "actions": []
}
""";
var rule = RuleJsonParser.Parse(json);
// Exception thrown when evaluating if "custom_op" not found in operators
```


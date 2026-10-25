# RuleLoom

Let your users weave their own business rules. You provide the threads (events, operators and actions); rules
written as JSON, or built in code, combine them without a deployment. When something happens in your application (an
order is placed, a price changes), RuleLoom runs the rules for that event against the current and previous state and
runs or schedules their actions.

- Conditions with `eq`, `gt`, `gte`, `lt`, `lte`, `changed`, `and`, `or`, `not`, plus your own operators
- Paths on .NET objects or JSON state (`JsonElement`, `JsonNode`), including the previous state (`$previous.Price`)
- Values compared by meaning: numbers across types, enums by name, ISO 8601 strings as dates
- Actions you write (`IAction`), with their own conditions and delayed or advance scheduling
- Validation that reports every problem with its JSON location; failures reported in results, not thrown

## Packages

| Package | Use it for |
|---|---|
| `RuleLoom` | The engine, built-in operators, validation |
| `RuleLoom.Abstractions` | Contracts and models only (no dependencies) |
| `RuleLoom.Extensions.DependencyInjection` | `services.AddRuleLoom(...)` |
| `RuleLoom.Schemas` | Event schemas (fields, labels, contexts) as JSON Schema, generated from your DTOs |

## Example

```json
{
  "id": "price-drop",
  "name": "Price drop",
  "eventType": "ProductUpdated",
  "condition": { "operator": "lt", "operands": [{ "path": "Price" }, { "path": "$previous.Price" }] },
  "actions": [{ "action": "send-email", "parameters": { "template": "price-drop" } }]
}
```

```csharp
services.AddRuleLoom(rules => rules.AddCatalog(catalog => catalog.AddAction<SendEmailAction>()));   // "send-email"

var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
var result = await engine.ExecuteAsync("ProductUpdated", current, previous, [RuleJsonParser.Parse(json)]);
```

## Documentation

- [Quick start](https://github.com/STotev/RuleLoom/blob/main/docs/guides/quick-start.md)
- [JSON rule format](https://github.com/STotev/RuleLoom/blob/main/docs/guides/json-rules.md)
- [Schemas and contexts](https://github.com/STotev/RuleLoom/blob/main/docs/guides/schemas-and-contexts.md)
- [Samples](https://github.com/STotev/RuleLoom/tree/main/samples)
- [Changelog](https://github.com/STotev/RuleLoom/blob/main/CHANGELOG.md) and
  [upgrading guide](https://github.com/STotev/RuleLoom/blob/main/docs/guides/upgrading.md)

RuleLoom is pre-1.0: minor versions can contain breaking changes, listed in the upgrading guide.

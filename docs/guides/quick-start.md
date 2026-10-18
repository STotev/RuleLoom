# RuleLoom Quick Start

RuleLoom runs **rules** against **events**: when something happens to an object in your app (a property is updated,
an order is placed), the rules for that event check conditions on the object's current and previous state and run or
schedule **actions**. Rules can be written in code or as JSON, so end users can author them.

> Upgrading from an earlier version? See [Upgrading](upgrading.md). The runnable version of this page is the
> [console sample](../../samples/RuleLoom.Samples.Console/).

## Install

```bash
dotnet add package RuleLoom
dotnet add package RuleLoom.Extensions.DependencyInjection   # optional: services.AddRuleLoom(...)
```

| Package | Use it for |
|---|---|
| `RuleLoom.Abstractions` | Contracts and models (no dependencies) |
| `RuleLoom` | The engine, built-in operators, `ConsoleLogAction`, `RuleValidator` |
| `RuleLoom.Extensions.DependencyInjection` | `services.AddRuleLoom(...)` |

## Your first rule

```csharp
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Actions;
using RuleLoom.Engine;
using RuleLoom.Evaluators;
using RuleLoom.Operators;

var rule = new RuleDefinition
{
    Id = "price-drop",
    Name = "Price drop",
    EventType = "ProductUpdated",
    Condition = new OperatorCondition
    {
        Operator = "lt",
        Operands = [new PathOperand { Path = "$current.Price" }, new PathOperand { Path = "$previous.Price" }]
    },
    Actions = [new ActionDefinition { Action = "console-log", Parameters = { ["message"] = "Price dropped!" } }]
};

var engine = new RuleEngine(
    new ConditionEvaluator(BuiltInOperators.Create()),   // eq, changed, and, or, not, gt, gte, lt, lte
    new DirectActionFactory([new ConsoleLogAction()]));         // registered as "console-log"

var previous = new { Price = 120m };
var current = new { Price = 99m };

RuleEngineResult result = await engine.ExecuteAsync("ProductUpdated", current, previous, [rule]);
```

The result tells you what happened to every rule for that event:

```csharp
foreach (var ruleResult in result.Rules)                       // Matched, NotMatched or Failed (with Error)
    foreach (var action in ruleResult.ActionResults)           // ExecutedImmediately, Scheduled, Skipped or Failed
        Console.WriteLine($"{ruleResult.RuleName}: {action.ActionName} {action.Status}");

if (result.HasFailures) { /* log or alert */ }
```

Failures are reported in the result, not thrown: one broken rule or action doesn't stop the others.

## Rules in JSON

```json
{
  "id": "price-drop",
  "name": "Price drop",
  "eventType": "ProductUpdated",
  "priority": 10,
  "condition": {
    "operator": "and",
    "operands": [
      { "operator": "changed", "operands": [{ "path": "Price" }] },
      { "operator": "lt", "operands": [{ "path": "$current.Price" }, { "path": "$previous.Price" }] }
    ]
  },
  "actions": [
    { "action": "console-log", "parameters": { "message": "Price dropped!" } }
  ]
}
```

```csharp
RuleDefinition rule = RuleJsonParser.Parse(json);              // or ParseMany / ParseAsync
```

- Paths read the current state (`"Price"`, `"Address.City"`); `$current.`/`$previous.` choose a state explicitly.
- Values are compared by meaning: `100` matches a `decimal`, `"active"` matches an enum, ISO 8601 strings match dates.
- Operator names, action names, event types and paths ignore case.
- Higher `priority` runs first.

Invalid JSON rules throw `RuleParseException` with the location of the problem. The [JSON rule guide](json-rules.md)
covers the format, paths and comparison rules in detail.

## Actions

Implement `IAction`. Rules refer to it by its class name without the `Action` suffix, in kebab-case:

```csharp
public sealed class SendEmailAction(IEmailService email) : IAction         // "action": "send-email"
{
    public async Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken ct = default)
    {
        var to = action.GetParameter<string>("to");                          // typed, with clear errors
        await email.SendAsync(to, action.GetParameter<string>("template"), ct);
        return ActionResult.Success();
    }
}
```

Use `[ActionName("notify")]` on the class for a different default, or give a name when registering:
`new DirectActionFactory().Add(new SendEmailAction(email), "notify")`.

An action can have its own `condition` (evaluated after the rule's) and a `schedule`:

```json
{ "action": "send-email", "parameters": { "template": "payment-reminder" },
  "schedule": { "mode": "Advance", "amount": 2, "unit": "Hours", "referencePath": "DueAt" } }
```

Scheduled actions aren't run: they come back as `Scheduled` with `ScheduledFor`, and `result.ScheduledActions` lists
them with their definitions so your scheduler can dispatch them.

## Custom operators

```csharp
public sealed class ContainsOperator : IConditionOperator
{
    public string Name => "contains";
    public int MinOperands => 2;
    public int MaxOperands => 2;

    public bool Evaluate(OperatorContext ctx) =>
        ctx.GetValue(0) is string text && ctx.GetValue(1) is string part && text.Contains(part, StringComparison.Ordinal);
}

var evaluator = new ConditionEvaluator([.. BuiltInOperators.Create(), new ContainsOperator()]);
```

`ctx.GetValue(i)` reads a value, `ctx.Test(i)` a condition, `ctx.GetPath(i)` a path, and `ctx.AreEqual` /
`ctx.TryCompare` use the shared comparison rules. Unit-test an operator by building an `OperatorContext` yourself:
`new OperatorContext(condition, new EvaluationContext(new { Title = "Summer sale" }))`.

## Dependency injection

```csharp
services.AddRuleLoom(rules => rules
    .AddAction<SendEmailAction>()            // resolved per scope, so it can inject scoped services
    .AddOperator<ContainsOperator>());       // built-in operators are registered automatically

// In a request or message-handler scope:
var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
```

`RemoveOperator("name")` and `RemoveBuiltInOperators()` adjust the operator set; `AddAction<T>("name")` registers an
action under another name.

## Contexts: related data

Rules can use data related to the event's subject, loaded only when a rule needs it:

```csharp
services.AddRuleLoom(rules => rules.AddContextProvider<CustomerProvider>("customer"));
```

```json
{ "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "Gold" }] }
```

See [Contexts](json-rules.md#contexts) for writing providers and how loading works.

## Event schemas

Describe each event in a schema class (package `RuleLoom.Schemas`): its fields come from your DTO, and it declares the
contexts it relates to and how it refers to them. Register schemas where the engine runs, so providers receive each
event's reference:

```csharp
public sealed class TransferMadeSchema : EventSchema<TransferDto>
{
    public TransferMadeSchema()
    {
        Event("TransferMade");
        Field(t => t.Amount).Label("Amount");
        Context("fromAccount").LoadedBy("account").ReferencedBy(t => t.FromAccountId).Label("From account");
        Context("toAccount").LoadedBy("account").ReferencedBy(t => t.ToAccountId).Label("To account");
    }
}

services.AddRuleLoom(rules => rules
    .AddSchemas(s => s.AddFromAssemblyOf<TransferMadeSchema>())     // sources; kept in memory unless .UseStore<MyStore>()
    .AddContextProvider<AccountProvider>("account"));                // loads both accounts, each with its own reference
```

With your own store (`.UseStore<MyStore>()`), the sources are saved into it when the app starts; in an app without a
host (e.g. a plain console app), call `await serviceProvider.SaveRuleLoomSchemasAsync()` once at startup.
Describe what a context offers once, for the provider that loads it; every event that relates to it shares the
description:

```csharp
public sealed class AccountSchema : ContextSchema<AccountDto>
{
    public AccountSchema()
    {
        Context("account").Label("Account");      // the provider name
        Field(a => a.Balance).Label("Balance");
        Field(a => a.InternalRiskScore).Ignore();
    }
}
```

`AddFromAssemblyOf<T>()` picks up event and context schema classes. `new SchemaCollection().AddFromAssemblyOf<TransferMadeSchema>().ToJson()`
exports them as JSON Schema documents, for another service or an authoring UI.

## Validating rules before saving them

```csharp
var validator = new RuleValidator(operators, actionFactory);     // or resolve RuleValidator from DI
foreach (var error in validator.Validate(rule).Errors)
    Console.WriteLine($"{error.Path}: {error.Message}");          // e.g. "$.actions[0].action: No action is registered under the name 'send-sms'."
```

## Next steps

- [JSON rule guide](json-rules.md): the rule format in depth
- [Console sample](../../samples/RuleLoom.Samples.Console/): runnable examples of everything above
- [Roadmap](../roadmap.md): what's next

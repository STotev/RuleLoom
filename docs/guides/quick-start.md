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
dotnet add package RuleLoom.AspNetCore                        # optional: app.MapRuleLoom("/rules")
```

| Package | Use it for |
|---|---|
| `RuleLoom.Schemas` | Event and context schemas, the catalog document and store (no dependencies) |
| `RuleLoom.Abstractions` | Contracts and models, including the `Catalog` |
| `RuleLoom` | The engine, `CatalogBuilder`, built-in operators, `ConsoleLogAction`, `RuleValidator` |
| `RuleLoom.Extensions.DependencyInjection` | `services.AddRuleLoom(...)` |
| `RuleLoom.AspNetCore` | `app.MapRuleLoom("/rules")`: the catalog and validation endpoints for an authoring UI |

## Your first rule

```csharp
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom;
using RuleLoom.Actions;
using RuleLoom.Engine;

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

// The catalog: what rules can use. It starts with the operators eq, changed, and, or, not, gt, gte, lt, lte.
var catalog = new CatalogBuilder()
    .AddAction<ConsoleLogAction>()                          // registered as "console-log"
    .Build();

var engine = new RuleEngine(catalog);

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

The **catalog** is the vocabulary rules are written in: event types (with their schemas), context providers,
operators and actions. It's built once and fixed afterwards; the engine and the validator run on it, and
`catalog.ToJson()` describes it for an authoring UI.

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
RuleDefinition rule = RuleJsonParser.Parse(json);              // or ParseMany / ParseEach / ParseAsync
```

- Paths read the current state (`"Price"`, `"Address.City"`); `$current.`/`$previous.` choose a state explicitly.
- Values are compared by meaning: `100` matches a `decimal`, `"active"` matches an enum, ISO 8601 strings match dates.
- Operator names, action names, event types and paths ignore case.
- Higher `priority` runs first.

Invalid JSON rules throw `RuleParseException` with the location of the problem. The [JSON rule guide](json-rules.md)
covers the format, paths and comparison rules in detail.

## Actions

Implement `IAction<TParameters>`: the rule's parameters arrive as a typed object. Rules refer to the action by its class
name without the `Action` suffix, in kebab-case:

```csharp
public sealed record SendEmailParameters(string To, string Template);   // both required: no defaults

public sealed class SendEmailAction(IEmailService email) : IAction<SendEmailParameters>   // "action": "send-email"
{
    public async Task<ActionResult> ExecuteAsync(SendEmailParameters parameters, ActionDefinition action,
        EvaluationContext context, CancellationToken ct = default)
    {
        await email.SendAsync(parameters.To, parameters.Template, ct);
        return ActionResult.Success();
    }
}
```

A parameter can be read from the event or a context when the action runs:
`"to": { "$path": "Customer.Email" }` (see [action parameters](json-rules.md#action-parameters)). Parameter names ignore
case, and enums are read by name. Parameters that can't be read as the type (a missing `to`, text
where a number belongs) fail only that action, with a message naming the parameter. The catalog describes the
parameters from the type, so an authoring UI can show a form for them.

An action can also implement `IAction` and read parameters one at a time with `action.GetParameter<T>("to")`, or all at
once with `action.GetParametersAsync<T>(context)`. Its parameters aren't described in the catalog.

Add it to the catalog with `AddAction<SendEmailAction>()`. Use `[ActionName("notify")]` on the class for a different
default, or give a name when adding it: `AddAction<SendEmailAction>("notify")`.

The engine creates actions with an `IActionFactory`. Without DI, `DirectActionFactory` creates actions that have a
parameterless constructor and returns the instances you add for the others:

```csharp
var engine = new RuleEngine(catalog, new DirectActionFactory().Add(new SendEmailAction(email)));
```

Describe an action for authoring UIs with an `ActionDescriptor<TAction, TParameters>` next to it (or
`ActionDescriptor<TAction>` for an action without typed parameters); the catalog finds it in the action's assembly:

```csharp
public sealed class SendEmailDescriptor : ActionDescriptor<SendEmailAction, SendEmailParameters>
{
    public SendEmailDescriptor()
    {
        Label("Send email");
        Description("Sends a templated email.");
        Parameter(p => p.To).Label("Recipient");
    }
}
```

An action can have its own `condition` (evaluated after the rule's) and a `schedule`:

```json
{ "action": "send-email", "parameters": { "to": "billing@example.com", "template": "payment-reminder" },
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

var catalog = new CatalogBuilder().AddOperator<ContainsOperator>().Build();
```

`RemoveOperator("name")` and `RemoveBuiltInOperators()` adjust the operator set, in call order. `ctx.GetValue(i)` reads a value, `ctx.Test(i)` a condition, `ctx.GetPath(i)` a path, and `ctx.AreEqual` /
`ctx.TryCompare` use the shared comparison rules. Unit-test an operator by building an `OperatorContext` yourself:
`new OperatorContext(condition, new EvaluationContext(new { Title = "Summer sale" }))`.

## Dependency injection

```csharp
services.AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
    .AddAction<SendEmailAction>()            // created in each scope, so it can inject scoped services
    .AddOperator<ContainsOperator>()));      // built-in operators are included

// In a request or message-handler scope:
var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
```

The catalog is built when the host starts, so a broken catalog stops the app from starting; in an app without a host
(e.g. a plain console app), call `await serviceProvider.StartRuleLoomAsync()` once at startup. Actions and context
providers are created in the scope: as registered, when you register their type yourself (do so for disposable ones),
otherwise with their dependencies from the scope.

## Contexts: related data

Rules can use data related to the event's subject, loaded only when a rule needs it. The event's schema declares
the context (next section) and a provider loads it:

```csharp
services.AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
    .AddEvent<OrderPlacedSchema>()                         // declares Context("customer")
    .AddContextProvider<CustomerProvider>("customer")));
```

```json
{ "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "Gold" }] }
```

See [Contexts](json-rules.md#contexts) for writing providers and how loading works.

## Event schemas

Describe each event in a schema class (package `RuleLoom.Schemas`): its fields come from your DTO, and it declares the
contexts it relates to and how it refers to them. Add the schemas to the catalog, so providers receive each event's
reference and the validator checks paths:

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

services.AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
    .AddFromAssemblyOf<TransferMadeSchema>()                         // event schemas, actions and their descriptions
    .AddContextProvider<AccountProvider>("account")));               // loads both accounts, each with its own reference
```

A context's data is described by its type: a typed provider's fields come from what it loads, and a
`ContextSchema<T>` (found next to the provider or its type) adds labels and leaves fields out. Every provider of the
type, and every event that relates to it, shares the schema:

```csharp
public sealed class AccountProvider(IAccounts accounts) : ContextProvider<AccountDto>
{
    protected override async ValueTask<AccountDto?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        await accounts.FindAsync(request.GetReference<string>()!, cancellationToken);
}

public sealed class AccountSchema : ContextSchema<AccountDto>
{
    public AccountSchema()
    {
        Label("Account");
        Field(a => a.Balance).Label("Balance");
        Field(a => a.InternalRiskScore).Ignore();
    }
}
```

For a collection, derive from `CollectionContextProvider<TItem>`. To keep the catalog in a database, implement
`ICatalogStore` and add `.UseStore<MyStore>()`: the catalog is built from your code plus what the store holds (code
wins), and saved back, at startup. `catalog.ToJson()` writes everything as one document for an authoring UI.

## Validating rules before saving them

```csharp
var validator = new RuleValidator(catalog);                       // or resolve RuleValidator from DI
foreach (var error in validator.Validate(rule).Errors)
    Console.WriteLine($"{error.Path}: {error.Message}");          // e.g. "$.actions[0].action: No action is registered under the name 'send-sms'."
```

The rule's event type must be in the catalog. Its paths are checked against the event's schema and its contexts'
schemas, a typed action's parameters against its parameters type, and comparisons that can never match are reported as
`Warnings`.

## Endpoints for an authoring UI

An ASP.NET Core app that serves its own rule-authoring UI can map the two endpoints it needs (with `AddRuleLoom`):

```csharp
app.MapRuleLoom("/rules")
   .RequireAuthorization();   // the route group: authorization stays yours
```

- `GET /rules/catalog` returns the catalog document.
- `POST /rules/validate` takes one rule or an array and returns, per rule, its `id` (and `index` for an array),
  whether it `parsed`, `isValid`, and its `errors` and `warnings`, each with a path relative to the rule:

```json
[
  { "index": 0, "id": "vip-order", "parsed": true, "isValid": true, "errors": [], "warnings": [] },
  { "index": 1, "parsed": false, "isValid": false, "errors": [{ "path": "$.condition", "message": "'condition' is required." }], "warnings": [] }
]
```

Arrays are parsed rule by rule, so a malformed rule doesn't hide the others' problems; JSON that can't be parsed, or
isn't a rule or an array, is a `400`. Rules aren't stored: saving them is your app's.

## Next steps

- [JSON rule guide](json-rules.md): the rule format in depth
- [Schemas and contexts](schemas-and-contexts.md): building a catalog step by step, with the ASP.NET Core sample
- [Console sample](../../samples/RuleLoom.Samples.Console/): runnable examples of everything above
- [Roadmap](../roadmap.md): what's next

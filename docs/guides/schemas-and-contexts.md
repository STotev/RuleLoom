# Schemas and Contexts

Rules are written in a vocabulary: the events they react to, the data related to those events, the operators that
compare values, and the actions they run. RuleLoom keeps that vocabulary in a **catalog**. The engine runs rules on it,
the validator checks rules against it, and a rule-authoring UI shows it to the people writing rules.

This guide builds a catalog step by step, using the [ASP.NET Core sample](../../samples/RuleLoom.Samples.AspNetCore/)
as the running example: an online shop whose rules react to orders and look up the customer's loyalty profile and order
history. The terms are defined in the [glossary](../../GLOSSARY.md).

## 1. Describe the events

An event schema says what an event looks like. Its fields come from the class the event is serialized from (how
System.Text.Json writes it), so you only add what the class can't say: the event's name, labels and descriptions, and
fields to leave out.

```csharp
public sealed record Order(string Number, string Status, decimal Total, int ItemCount, DateTimeOffset? PaymentDueAt, Person Customer);

public sealed class OrderPlacedSchema : EventSchema<Order>
{
    public OrderPlacedSchema()
    {
        Event("OrderPlaced").Label("Order placed");
        Field(o => o.Total).Label("Order total");          // optional
    }
}
```

Two events can share a class (`OrderPlaced` and `OrderUpdated` both carry an `Order`); each gets its own schema.
Schemas live in `RuleLoom.Schemas`, a package with no dependencies, so the project that defines your events doesn't
need the engine.

## 2. Declare the related data: contexts

Rules often need data the event doesn't carry: the customer's loyalty tier, their earlier orders. Instead of sending it
with every event, the event's schema declares it as a **context**, and says how the event refers to it:

```csharp
public OrderPlacedSchema()
{
    Event("OrderPlaced").Label("Order placed");
    Context("customer").ReferencedBy(o => o.Customer.Email).Label("Customer");
    Context("orderHistory").ReferencedBy(o => o.Customer.Email).Label("Earlier orders");
}
```

- **Rules can use only the contexts their event declares.** `@customer.Tier` works in a rule on `OrderPlaced`; on an
  event that doesn't declare `customer`, the validator rejects it and the engine fails the rule.
- **`ReferencedBy`** names the field (or fields: `o => new { o.Zone, o.Date }`) that identifies the related data. The
  engine reads it from each event and hands it to the provider.
- **`LoadedBy("provider")`** lets one provider load several contexts, e.g. `fromAccount` and `toAccount` both loaded by
  `account`. Without it, the provider registered under the context's own name loads it.

## 3. Load contexts: context providers

A context provider loads a context when a rule or an action needs it. Derive from `ContextProvider<T>` for one entity
or `CollectionContextProvider<TItem>` for a list, and read the event's reference with `GetReference<T>()`:

```csharp
public sealed class CustomerProvider(CustomerDirectory customers) : ContextProvider<CustomerProfile>
{
    protected override ValueTask<CustomerProfile?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(customers.FindProfile(request.GetReference<string>()!));
}

public sealed class OrderHistoryProvider(CustomerDirectory customers) : CollectionContextProvider<PastOrder>
{
    protected override ValueTask<IReadOnlyList<PastOrder>> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(customers.FindOrders(request.GetReference<string>()!));
}
```

Return `null` when there's nothing to load (a guest has no profile): paths into a missing context resolve to `null`.
For data that has no .NET type, such as JSON from another service, implement `IContextProvider` instead. How loading
works (once per event, in parallel, failures kept to the rules that use the context) is described under
[Contexts](json-rules.md#contexts).

## 4. Describe the context data

A typed provider's fields come from the type it loads. A `ContextSchema<T>` of that type adds labels and descriptions,
and can leave fields out:

```csharp
public sealed class CustomerProfileSchema : ContextSchema<CustomerProfile>
{
    public CustomerProfileSchema()
    {
        Label("Customer");
        Description("The customer's loyalty profile.");
        Field(c => c.Tier).Label("Loyalty tier").Description("bronze, silver or gold");
        Field(c => c.MemberSince).Label("Member since");
    }
}
```

The schema describes the data, not the provider: every provider that loads a `CustomerProfile` shares it. Put it next
to the provider or the type, and the catalog finds it.

## 5. Describe the actions

Schemas describe data; **descriptors** describe code. An `ActionDescriptor<TAction>` gives an action a label and a
description for rule authors. Ship it next to the action:

```csharp
public sealed class LoyaltyCheckDescriptor : ActionDescriptor<LoyaltyCheckAction>
{
    public LoyaltyCheckDescriptor()
    {
        Label("Loyalty check");
        Description("Reports the customer's earlier orders: how many and their total.");
    }
}
```

An action with typed parameters (`IAction<TParameters>`) is described by its parameters type too, with or without a
descriptor: the catalog document lists each parameter, its type and whether it's required. Derive from
`ActionDescriptor<TAction, TParameters>` to label them with `Parameter(p => p.To).Label("Recipient")`.

Operators describe themselves (`Label` and `Description` on `IConditionOperator`); the built-in ones already do.

## 6. Build the catalog

With dependency injection, configure the catalog in `AddRuleLoom`. It's built when the host starts:

```csharp
builder.Services
    .AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
        .AddFromAssemblyOf<OrderPlacedSchema>()                    // event schemas, actions, and their descriptions
        .AddContextProvider<CustomerProvider>("customer")
        .AddContextProvider<OrderHistoryProvider>("orderHistory")
        .AddAction<ConsoleLogAction>()))                           // built in, for development
    .AddSingleton<CustomerDirectory>();
```

- `AddFromAssemblyOf<T>()` adds the public event schemas and actions in `T`'s assembly. Add them one at a time with
  `AddEvent<TSchema>()` and `AddAction<TAction>(name?)`. Context providers are always added by name.
- Descriptions are found in the assembly of what they describe; pass one explicitly with
  `AddAction<TAction, TDescriptor>()` or `AddContextProvider<TProvider, TSchema>(name)`.
- **A broken catalog stops the app from starting**: an event declaring a context with no provider, a `ReferencedBy`
  that isn't a field, two actions with the same name. The error lists every problem.
- Actions and providers are created per scope, with their dependencies.

Without DI, build it yourself and hand it to the engine and the validator:

```csharp
var catalog = new CatalogBuilder()
    .AddEvent<OrderPlacedSchema>()
    .AddContextProvider<CustomerProvider>("customer")
    .AddContextProvider<OrderHistoryProvider>("orderHistory")
    .AddAction<LoyaltyCheckAction>()
    .Build();

var engine = new RuleEngine(catalog,
    new DirectActionFactory().Add(new LoyaltyCheckAction(logger)),
    new DirectContextProviderFactory().Add(new CustomerProvider(customers)).Add(new OrderHistoryProvider(customers)));
var validator = new RuleValidator(catalog);
```

## 7. Use contexts in rules and actions

Rules read a context with `@name.Path`. The engine loads only the contexts the event's rules use:

```json
{
  "id": "vip-order",
  "name": "VIP order",
  "eventType": "OrderPlaced",
  "condition": {
    "operator": "and",
    "operands": [
      { "operator": "gte", "operands": [{ "path": "Total" }, { "value": 500 }] },
      { "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "gold" }] }
    ]
  },
  "actions": [{ "action": "send-email", "parameters": { "to": "Customer.Email", "subject": "A gift is on its way" } }]
}
```

Rules can't use paths into a list, so collections are for actions. An action loads any context its event declares:

```csharp
public sealed class LoyaltyCheckAction(ILogger<LoyaltyCheckAction> logger) : IAction
{
    public async Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var orders = await context.GetContextAsync<IReadOnlyList<PastOrder>>("orderHistory", cancellationToken) ?? [];
        var message = $"{orders.Count} earlier orders worth {orders.Sum(o => o.Total):0.00}.";

        logger.LogInformation("Loyalty check: {Message}", message);

        return ActionResult.Success(message);
    }
}
```

## 8. Validate rules against the catalog

Validate rules when they're saved (or, as the sample does, when they're loaded) so authors find mistakes before an
event does:

```csharp
var result = validator.Validate(rule);
foreach (var error in result.Errors)
    Console.WriteLine($"{error.Path}: {error.Message}");
```

The validator checks the rule against the catalog:

- the event type is in the catalog, and the operators and actions exist, with the right number of operands;
- a typed action's parameters match its parameters type: no unknown parameters, every required one, values of the right
  type;
- paths are fields of the event or of the contexts it declares. In the sample, a typo in a context path is reported
  with its location: `$.condition.operands[0].path: 'Teir' is not a field of '@customer'.`;
- comparisons that can never match (a number against text) are `Warnings`, which don't make a rule invalid.

## 9. Show the catalog to rule authors

`catalog.ToJson()` writes the whole vocabulary as one document, ready for an authoring UI. The sample serves it at
`GET /catalog`:

```csharp
app.MapGet("/catalog", (Catalog catalog) => Results.Text(catalog.ToJson(), "application/json"));
```

```json
{
  "events": [
    {
      "x-ruleloom-event": "OrderPlaced",
      "title": "Order placed",
      "type": "object",
      "properties": { "total": { "type": "number" }, "...": {} },
      "x-ruleloom-contexts": {
        "customer": { "title": "Customer", "x-ruleloom-provider": "customer", "x-ruleloom-referenced-by": "customer.email" },
        "orderHistory": { "title": "Earlier orders", "x-ruleloom-provider": "orderHistory", "x-ruleloom-referenced-by": "customer.email" }
      }
    }
  ],
  "contexts": [
    {
      "x-ruleloom-context": "customer",
      "title": "Customer",
      "type": "object",
      "properties": { "tier": { "type": "string", "title": "Loyalty tier" }, "...": {} }
    }
  ],
  "operators": [{ "name": "eq", "label": "equals", "description": "...", "minOperands": 2, "maxOperands": 2 }],
  "actions": [{ "name": "loyalty-check", "label": "Loyalty check", "description": "..." }],
  "problems": []
}
```

Events and contexts are JSON Schema 2020-12 documents. A context's schema is listed under the name of the provider that
loads it, and every context declaration names that provider in `x-ruleloom-provider` (the one set with `LoadedBy`, or
else the context's own name), so a UI finds a context's schema without a default rule. Documents written without it
are read as naming the context's own name.

## 10. Keeping the catalog in a store

A catalog store keeps the catalog document outside the app, in a database for example: `UseStore<MyStore>()`
(implement `ICatalogStore`). At startup the catalog is built from your code plus the event and context schemas the
store holds (code wins), and the result is saved back. Saves add or update entries and never delete.

You need one when something other than the running app reads the catalog, such as an authoring UI deployed on its own,
or when schemas come from outside your code. An app that serves its own UI, like the sample, doesn't. Stored entries
that don't fit (a context without a provider) don't stop the app; they're listed as `problems` and fail only the rules
that use them.

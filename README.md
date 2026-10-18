<p align="center">
  <img src="docs/assets/logo.svg" alt="RuleLoom" width="360">
</p>

<p align="center">
  <b>Let your users weave their own business rules.</b><br>
  Event-driven rules for .NET, written as JSON and run against your objects or JSON events.
</p>

<p align="center">
  <a href="https://github.com/STotev/RuleLoom/actions/workflows/ci.yml"><img src="https://github.com/STotev/RuleLoom/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://www.nuget.org/packages/RuleLoom"><img src="https://img.shields.io/nuget/vpre/RuleLoom?label=NuGet" alt="NuGet"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="License: MIT"></a>
</p>

---

You provide the threads: the **events** your application raises, the **operators** rules can use and the **actions**
they can take. Your users weave them into **rules**, without a deployment. When something happens (an order is
placed, a price changes), RuleLoom runs that event's rules against the current and previous state and runs or
schedules their actions.

- **Rules as data**: JSON rules you can store per customer, edit in a UI and validate before saving, or rules built in code.
- **Any state**: paths resolve on .NET objects and on JSON (`JsonElement`, `JsonNode`), so a central service can run
  rules on events from other services without their classes.
- **Change-aware**: compare with the previous state (`changed`, `$previous.Price`), sent in full or as just the
  changed fields.
- **Related data on demand**: `@customer.Tier` is loaded by a provider you register, only when a rule uses it.
- **Comparison by meaning**: `100` equals `100.0`, enums match by name, ISO 8601 strings compare as dates.
- **Your actions, your scheduler**: actions are classes you write; delayed actions come back with their due time for
  Hangfire, Quartz.NET or any scheduler you use.
- **Clear failures**: one failing rule or action doesn't stop the others; results and validation errors say exactly
  what went wrong and where.
- **Small footprint**: `RuleLoom.Abstractions` has no dependencies outside .NET; storage, tenancy and transport stay yours.

## Install

```bash
dotnet add package RuleLoom
dotnet add package RuleLoom.Extensions.DependencyInjection   # optional: services.AddRuleLoom(...)
```

| Package | Use it for |
|---|---|
| `RuleLoom` | The engine, built-in operators, validation |
| `RuleLoom.Abstractions` | Contracts and models only, e.g. for projects that write actions or operators |
| `RuleLoom.Extensions.DependencyInjection` | Registration with `Microsoft.Extensions.DependencyInjection` |
| `RuleLoom.Schemas` | Describing events (fields, labels, contexts) as JSON Schema, for producers and central rules services |

Requires .NET 10.

## Example

A rule, woven in JSON:

```json
{
  "id": "vip-order",
  "name": "VIP order",
  "eventType": "OrderPlaced",
  "condition": {
    "operator": "and",
    "operands": [
      { "operator": "gte", "operands": [{ "path": "Total" }, { "value": 500 }] },
      { "operator": "eq", "operands": [{ "path": "Customer.Tier" }, { "value": "Gold" }] }
    ]
  },
  "actions": [
    { "action": "send-email", "parameters": { "template": "vip-thank-you" } },
    {
      "action": "send-email",
      "parameters": { "template": "payment-reminder" },
      "schedule": { "mode": "Advance", "amount": 1, "unit": "Days", "referencePath": "PaymentDueAt" }
    }
  ]
}
```

An action, and the engine:

```csharp
public sealed class SendEmailAction(IEmailService email) : IAction        // "action": "send-email"
{
    public async Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken ct)
    {
        await email.SendAsync(action.GetParameter<string>("template"), context.Current, ct);
        return ActionResult.Success();
    }
}

services.AddRuleLoom(rules => rules.AddAction<SendEmailAction>());

// When an order is placed (rules come from your storage):
var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
var result = await engine.ExecuteAsync("OrderPlaced", order, previous: null, rules, ct);

foreach (var scheduled in result.ScheduledActions)                        // the reminder, due a day before PaymentDueAt
    scheduler.Schedule(scheduled.Action, scheduled.Result.ScheduledFor);
```

`order` can be a .NET object or JSON received from another service. `result.Rules` reports every rule for the event
(`Matched`, `NotMatched` or `Failed`) and every action (`ExecutedImmediately`, `Scheduled`, `Skipped` or `Failed`).

## Operators

| Operator | Meaning | Operands |
|---|---|---|
| `eq` | Equal ([comparison rules](docs/guides/json-rules.md#comparing-values)) | 2 |
| `gt`, `gte`, `lt`, `lte` | Ordering: numbers, dates, strings | 2 |
| `changed` | The value differs from the previous state | 1 (a path) |
| `and`, `or` | All / any of the conditions | 1 or more |
| `not` | The condition is false | 1 |

Add your own by implementing `IConditionOperator`; see the [quick start](docs/guides/quick-start.md#custom-operators).

## Documentation

- [Quick start](docs/guides/quick-start.md): rules in code and JSON, actions, operators, DI, validation
- [JSON rule format](docs/guides/json-rules.md): the format, paths, rules on JSON state, comparison rules, scheduling
- [Upgrading](docs/guides/upgrading.md): breaking changes by version, with before/after examples
- Samples: a [console sample](samples/RuleLoom.Samples.Console/) and a
  [central rules service](samples/RuleLoom.Samples.RulesService/) (ASP.NET Core, rules per tenant, Quartz.NET)
- [Vision](docs/vision.md) and [roadmap](docs/roadmap.md): where RuleLoom is going and why
- [Changelog](CHANGELOG.md)

## Status

RuleLoom is **pre-1.0**: the API is settling, and minor versions can contain breaking changes, each listed in the
[upgrading guide](docs/guides/upgrading.md). Next on the [roadmap](docs/roadmap.md): event schemas and a fluent rule
builder (0.6), hosting packages and an event envelope (0.7), and support for running scheduled actions later (0.8).

## Contributing

Issues and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers the development setup, tests and how
changes are proposed; please report security issues as described in [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)

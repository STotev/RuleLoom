# ASP.NET Core Sample

RuleLoom hosted in ASP.NET Core: other services post their events as JSON, and this service runs each tenant's rules
against them. It shows:

- **JSON state**: the event's `current` and `previous` stay `JsonElement`s; the service has no producer classes.
- **Tenancy and storage are yours**: each tenant's rules are JSON files in `Rules/{tenantId}/`, read for every event, so
  edits apply to the next one.
- **Validate, then run**: rules are checked with `RuleValidator` against the catalog; invalid ones are reported in the
  response and skipped.
- **Contexts**: orders declare the customer's loyalty profile (`@customer.Tier`, used in a rule) and their earlier
  orders (`orderHistory`, a collection read by an action), loaded by context providers.
- **The catalog document**: `GET /catalog` returns what a rule-authoring UI would read.
- **Scheduled actions** are handed to Quartz.NET (in-memory store) and run when due.

It's also a spike: what was awkward while building it is recorded in the
[spec's findings](../../docs/specs/2026-10-14-aspnetcore-sample-design.md#findings).

## Run it

```bash
dotnet run --project samples/RuleLoom.Samples.AspNetCore
```

The service listens on `http://localhost:5080`. Send the requests in [`AspNetCore.http`](AspNetCore.http) from
Rider (its pre-request script sets a payment due time two minutes from now), and watch the console: emails are
logged by `SendEmailAction`, and scheduled actions log a line when they run. The
[schemas and contexts guide](../../docs/guides/schemas-and-contexts.md) walks through the catalog's code.

## Wire format

`POST /events`:

```json
{
  "eventType": "OrderUpdated",
  "tenantId": "acme-shop",
  "entityId": "order-1001",
  "current": { "Status": "Paid", "Customer": { "Email": "ada@example.com" } },
  "previous": { "Status": "AwaitingPayment", "Customer": { "Email": "ada@example.com" } }
}
```

`previous` is optional. `entityId` identifies what the event is about; it's part of the scheduled jobs' keys. This is
the sample's own format, not a RuleLoom contract.

The response lists every rule for the event type with its status and action results, the tenant's invalid rules, and
the jobs handed to Quartz. An unknown tenant gets a 404.

`GET /catalog` returns the catalog document: the events (JSON Schema, with the contexts they declare), the context
schemas, the operators and the actions, with their labels.

## Tenants

| Tenant | Event | Rule | Actions |
|---|---|---|---|
| `acme-shop` | `OrderPlaced` | VIP order (`Total gte 500` and `@customer.Tier eq gold`) | Email the customer |
| | `OrderPlaced` | Payment reminder (`Status eq AwaitingPayment`) | Email scheduled 1 minute before `PaymentDueAt` |
| | `OrderPlaced` | Big basket (`ItemCount gte 5`) | `console-log` |
| | `OrderPlaced` | Loyalty check (`Total gte 100`) | `loyalty-check`: reads `orderHistory` |
| | `OrderPlaced` | Silver upgrade | **Invalid**: `@customer.Teir` isn't a field of the customer context |
| | `OrderUpdated` | Order status changed (`changed Status`) | Email the customer |
| | `OrderUpdated` | Shipped SMS | **Invalid**: no `send-sms` action is registered |
| `wheels-market` | `AdPublished` | Ad published (`Status eq Published`) | `console-log`; email the seller after 30 seconds |
| | `AdUpdated` | Price drop (`Price lt $previous.Price`) | Email the seller; `console-log` if the price is under 10,000 (action condition) |

## How it fits together

| File | Role |
|---|---|
| `Program.cs` | Registers RuleLoom (`AddRuleLoom` with a catalog of the event schemas, the context providers and the actions) and Quartz; maps `GET /catalog` and `POST /events` |
| `Events.cs` | The events the app raises and their schemas, with the contexts orders declare: rules on other event types are rejected |
| `Customers.cs` | The `customer` and `orderHistory` context providers, the customer profile's schema, and an in-memory customer directory (`ada@example.com` is gold with three earlier orders, `bob@example.com` bronze with none) |
| `TenantRuleStore.cs` | Reads, parses and validates a tenant's rule files |
| `ActionScheduler.cs` | One Quartz job per scheduled action, keyed `{tenant}/{entity}/{rule}/{action index}`; the same event sent again replaces the pending job |
| `RunScheduledActionJob.cs` | Runs the action when due, with the state of the event that scheduled it (conditions aren't checked again) |
| `SendEmailAction.cs` | A custom action that logs an email; `to` is a path to the address in the state. Its descriptor labels it in the catalog |
| `LoyaltyCheckAction.cs` | A custom action that reads the `orderHistory` collection context, with its descriptor |

Pending jobs live in memory and are lost on restart.

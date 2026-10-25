# ASP.NET Core Sample

RuleLoom hosted in ASP.NET Core: other services post their events as JSON, and this service runs each tenant's rules
against them. It shows:

- **JSON state**: the event's `current` and `previous` stay `JsonElement`s; the service has no producer classes.
- **Tenancy and storage are yours**: each tenant's rules are JSON files in `Rules/{tenantId}/`, read for every event, so
  edits apply to the next one.
- **Validate, then run**: rules are checked with `RuleValidator`; invalid ones are reported in the response and
  skipped.
- **Scheduled actions** are handed to Quartz.NET (in-memory store) and run when due.

It's also a spike: what was awkward while building it is recorded in the
[spec's findings](../../docs/specs/2026-10-14-aspnetcore-sample-design.md#findings).

## Run it

```bash
dotnet run --project samples/RuleLoom.Samples.AspNetCore
```

The service listens on `http://localhost:5080`. Send the requests in [`AspNetCore.http`](AspNetCore.http) from
Rider (its pre-request script sets a payment due time two minutes from now), and watch the console: emails are
logged by `SendEmailAction`, and scheduled actions log a line when they run.

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

## Tenants

| Tenant | Event | Rule | Actions |
|---|---|---|---|
| `acme-shop` | `OrderPlaced` | VIP order (`Total gte 500`) | Email the customer |
| | `OrderPlaced` | Payment reminder (`Status eq AwaitingPayment`) | Email scheduled 1 minute before `PaymentDueAt` |
| | `OrderPlaced` | Big basket (`ItemCount gte 5`) | `console-log` |
| | `OrderUpdated` | Order status changed (`changed Status`) | Email the customer |
| | `OrderUpdated` | Shipped SMS | **Invalid**: no `send-sms` action is registered |
| `wheels-market` | `AdPublished` | Ad published (`Status eq Published`) | `console-log`; email the seller after 30 seconds |
| | `AdUpdated` | Price drop (`Price lt $previous.Price`) | Email the seller; `console-log` if the price is under 10,000 (action condition) |

## How it fits together

| File | Role |
|---|---|
| `Program.cs` | Registers RuleLoom (`AddRuleLoom` with a catalog of the event schemas, `send-email` and `console-log`) and Quartz; maps `POST /events` |
| `Events.cs` | The events the app raises and their schemas: rules on other event types are rejected |
| `TenantRuleStore.cs` | Reads, parses and validates a tenant's rule files |
| `ActionScheduler.cs` | One Quartz job per scheduled action, keyed `{tenant}/{entity}/{rule}/{action index}`; the same event sent again replaces the pending job |
| `RunScheduledActionJob.cs` | Runs the action when due, with the state of the event that scheduled it (conditions aren't checked again) |
| `SendEmailAction.cs` | A custom action that logs an email; `to` is a path to the address in the state |

Pending jobs live in memory and are lost on restart.

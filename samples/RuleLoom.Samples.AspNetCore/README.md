# ASP.NET Core Sample

RuleLoom hosted in ASP.NET Core: other services post their events as JSON, and this service runs each tenant's rules
against them. It shows:

- **JSON state**: the event's `current` and `previous` stay `JsonElement`s; the service has no producer classes.
- **Tenancy and storage are yours**: each tenant's rules are JSON files in `Rules/{tenantId}/`, read for every event, so
  edits apply to the next one.
- **Validate, then run**: each file is parsed rule by rule (`RuleJsonParser.ParseEach`) and the rules are checked with
  `RuleValidator` against the catalog; malformed and invalid rules are reported in the response and skipped, and the
  rest of the file still runs.
- **Contexts**: orders declare the customer's loyalty profile (`@customer.Tier`, used in a rule) and their earlier
  orders (`orderHistory`, a collection read by an action), loaded by context providers.
- **Typed actions and paths in parameters**: `send-email` receives `SendEmailParameters`; rules give the recipient as
  a path, `"to": { "$path": "Customer.Email" }` or `{ "$path": "@customer.Email" }`, which RuleLoom resolves (loading
  the context) and the validator checks.
- **Authoring endpoints**: `app.MapRuleLoom("/rules")` serves the catalog document (`GET /rules/catalog`) and validates
  rules (`POST /rules/validate`), as a rule-authoring UI would use them.
- **Scheduled actions, checked when due**: payment reminders are handed to Quartz.NET (stored as JSON) and run with one
  call, `ExecuteScheduledAsync`, which checks the rule again against the order **as it is then**: order events declare
  their subject (`Subject(o => o.Number).LoadedBy("order")`), so a reminder for an order paid meanwhile is skipped.
  Reminders more than a minute late are skipped too (`SkipOverdueActions`). See the
  [dispatching guide](../../docs/guides/dispatching-scheduled-actions.md).
- **Observability**: RuleLoom's spans (under each request's span) and metrics go to the OpenTelemetry console exporter, and every rule's and action's outcome
  to the Debug log, written by Serilog with timestamps and each log's source (levels and the console format are in the
  `Serilog` section of `appsettings.json`; `"RuleLoom": "Debug"`).

It's also a spike: what was awkward while building it is recorded in the
[spec's findings](../../docs/specs/2026-10-14-aspnetcore-sample-design.md#findings).

## Run it

```bash
dotnet run --project samples/RuleLoom.Samples.AspNetCore
```

The service listens on `http://localhost:5080`. Send the requests in [`AspNetCore.http`](AspNetCore.http) from
Rider (its pre-request scripts set payment due times two minutes from now), and watch the console: emails are
logged by `SendEmailAction`, and scheduled actions log a line when they run.

**The scheduling demo**: send the first order (SO-1001, Ada, awaiting payment), the unpaid order (SO-1003, Bob), then
the update that marks SO-1001 as paid. About a minute later both reminders are due: Bob's is sent, and Ada's is skipped,
"rule 'payment-reminder' no longer matches", because the order loaded then is paid. The
[schemas and contexts guide](../../docs/guides/schemas-and-contexts.md) walks through the catalog's code.

## Wire format

`POST /events`:

```json
{
  "eventType": "OrderUpdated",
  "tenantId": "acme-shop",
  "entityId": "order-1001",
  "eventId": "msg-1001-paid",
  "occurredAt": "2026-11-17T09:00:00Z",
  "current": { "Number": "SO-1001", "Status": "Paid", "Customer": { "Email": "ada@example.com" } },
  "previous": { "Status": "AwaitingPayment", "Customer": { "Email": "ada@example.com" } }
}
```

`previous`, `eventId` and `occurredAt` are optional. The sample turns a request into a `RuleEvent` with the tenant id in
its `Metadata` and `entityId` as its `Subject` (order events declare theirs, the order number, in their schema). This is
the sample's own format, not a RuleLoom contract. Order events also update the sample's `OrderStore`, which stands in
for the shop's database that the producer updated before sending the event.

The response lists every rule for the event type with its status and action results, the tenant's invalid rules, and
the jobs handed to Quartz. An unknown tenant gets a 404.

`GET /rules/catalog` returns the catalog document: the events (JSON Schema, with the contexts they declare), the
context schemas, the operators and the actions, with their labels and parameters. `POST /rules/validate` takes a rule or
an array of rules and returns, per rule, its errors and warnings with their paths; the `.http` file has examples. Both
come from `MapRuleLoom`; a real app secures them with `RequireAuthorization()`.

## Tenants

| Tenant | Event | Rule | Actions |
|---|---|---|---|
| `acme-shop` | `OrderPlaced` | VIP order (`Total gte 500` and `@customer.Tier eq gold`) | Email the customer, at the address in their profile (`@customer.Email`) |
| | `OrderPlaced` | Payment reminder (`Status eq AwaitingPayment`) | Email scheduled 1 minute before `PaymentDueAt` |
| | `OrderPlaced` | Big basket (`ItemCount gte 5`) | `console-log` |
| | `OrderPlaced` | Loyalty check (`Total gte 100`) | `loyalty-check`: reads `orderHistory` |
| | `OrderPlaced` | Silver upgrade | **Invalid**: `@customer.Teir` isn't a field of the customer context |
| | `OrderPlaced` | Gift wrap | **Malformed**: no `condition` (reported at `$[4].condition`; the file's other rules still run) |
| | `OrderUpdated` | Order status changed (`changed Status`) | Email the customer |
| | `OrderUpdated` | Shipped SMS | **Invalid**: no `send-sms` action is registered |
| | `OrderUpdated` | Review request | **Invalid**: `send-email` has no parameter `subjet`, and `subject` is missing |
| `wheels-market` | `AdPublished` | Ad published (`Status eq Published`) | `console-log`; email the seller after 30 seconds |
| | `AdUpdated` | Price drop (`Price lt $previous.Price`) | Email the seller; `console-log` if the price is under 10,000 (action condition) |

## How it fits together

| File | Role |
|---|---|
| `Program.cs` | Registers RuleLoom (`AddRuleLoom` with a catalog of the event schemas, the context providers and the actions, and `Configure` for overdue actions), Quartz and OpenTelemetry; maps the authoring endpoints (`MapRuleLoom("/rules")`) and `POST /events` |
| `Events.cs` | The events the app raises and their schemas: the subject orders declare (loaded by `order`) and their contexts; rules on other event types are rejected |
| `Orders.cs` | The in-memory `OrderStore` (the shop's database, kept current by order events) and the `order` provider that loads an order as it is now |
| `Customers.cs` | The `customer` and `orderHistory` context providers, the customer profile's schema, and an in-memory customer directory (`ada@example.com` is gold with three earlier orders, `bob@example.com` bronze with none) |
| `TenantRuleStore.cs` | Reads, parses and validates a tenant's rule files |
| `ActionScheduler.cs` | One Quartz job per scheduled action, job and trigger keyed `{tenant}/{ScheduledAction.Key}` (`{rule}:{action index}:{subject}`), its data stored as JSON; the same event sent again replaces the pending job |
| `RunScheduledActionJob.cs` | Runs a scheduled action when due with one call, `RuleEngine.ExecuteScheduledAsync`, given the event that scheduled it and the tenant's current rules: the rule and the action are checked again against the order loaded then, and the rule's current version runs (or is skipped, with the reason) |
| `SendEmailAction.cs` | A typed action (`IAction<SendEmailParameters>`) that logs an email; its descriptor labels it and its parameters in the catalog |
| `LoyaltyCheckAction.cs` | A custom action that reads the `orderHistory` collection context, with its descriptor |

Pending jobs live in Quartz's in-memory store and are lost on restart; their data is JSON, so a persistent Quartz store
(e.g. ADO.NET) would work without code changes.

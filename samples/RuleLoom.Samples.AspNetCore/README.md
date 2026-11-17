# ASP.NET Core Sample

A small web shop whose business rules run on RuleLoom. The shop has its own API (place, pay and ship orders); each
change raises an event to RuleLoom in-process, and the rules (JSON files) send emails, now or later. A demo page shows
the orders, what the rules did and the emails sent.

## Run it

```bash
dotnet run --project samples/RuleLoom.Samples.AspNetCore
```

Open **http://localhost:5080**. Or send the requests in [`AspNetCore.http`](AspNetCore.http).

## A demo in five steps

1. **Place an order for Ada** (gold, 640.50, 5 items). "VIP order" emails her (the rule reads her tier from the
   `customer` context), "Big basket" and "Loyalty check" run, and "Payment reminder" is scheduled 30 seconds before
   payment is due.
2. **Place an order for Bob** (bronze, 75.00). Only the reminder is scheduled.
3. **Pay Ada's order.** "Order paid" emails her; `changed` sees the status change between the order before and after.
4. **Wait 30 seconds.** Both reminders come due. Bob's is sent; Ada's is skipped: RuleLoom loaded her order as it is
   *now* and the rule no longer matches.
5. **Edit a rule** in `Rules/orders.json` (say, the VIP threshold) and place another order: the next event uses it. Break
   one (misspell a field) and it moves to "Left out" on the page, with the problem's location.

Also: **ship** an order ("Order shipped" emails now and asks for a review 20 seconds later), and place an order with
payment due in 25 or 15 seconds: its reminder is already late, and is sent at once (5 seconds late) or skipped (15
seconds late, past the 10-second grace period).

## What it shows

- **Events raised in-process**: the shop saves the order and hands RuleLoom the order before and after
  (`RuleRunner`). The order number is its only id: order events declare it as their subject.
- **Rules as JSON files**, read for every event, so edits apply at once. Each file is parsed rule by rule and checked
  against the catalog; rules with mistakes (`Rules/mistakes.json`) are left out and listed, and the rest run.
- **Contexts**: rules read the customer's profile (`@customer.Tier`); `loyalty-check` reads their earlier orders.
- **Typed actions and paths in parameters**: `send-email` receives `SendEmailParameters`; rules give the recipient as
  a path, `{ "$path": "Customer.Email" }` or `{ "$path": "@customer.Email" }`.
- **Scheduled actions, checked again when due**: Quartz.NET runs each with one call, `ExecuteScheduledAsync`; job data
  is JSON, so a persistent Quartz store works too. Actions more than 10 seconds late are skipped
  (`SkipOverdueActions`). See the [dispatching guide](../../docs/guides/dispatching-scheduled-actions.md).
- **Authoring endpoints**: `MapRuleLoom("/rules")` serves the catalog (`GET /rules/catalog`) and validates rules
  (`POST /rules/validate`), as a rule-authoring UI would use them.
- **Logs**: Serilog on the console: emails sent, scheduled actions run, and RuleLoom's warnings. OpenTelemetry is set
  up in `Program.cs` but commented out (the console exporter buries the logs); uncomment it to see the spans and metrics.

Storing rules, tenants and the like are the app's choice: this shop keeps one set of rules in files.

## How it fits together

| Folder | Holds |
|---|---|
| `Program.cs` | Registration (`AddRuleLoom` with the catalog and overdue actions, Quartz, the shop's services) and endpoint mapping |
| `Orders/` | `Order`, the `OrderPlaced`/`OrderUpdated` schemas (subject, contexts), `OrderStore`, the `order` subject loader and the shop's endpoints |
| `Customers/` | The customer directory, the `customer` and `orderHistory` context providers and the profile's schema |
| `Actions/` | `send-email` (typed, with its descriptor) and `loyalty-check` |
| `Rules/` | The rule files, `RuleStore` (read, parse, validate) and `RuleRunner` (an event to RuleLoom, its scheduled actions to Quartz) |
| `Scheduling/` | `ActionScheduler` (a Quartz job per scheduled action, keyed by `ScheduledAction.Key`) and `RunScheduledActionJob` |
| `Demo/` | The activity feed, the outbox and the endpoints the page reads (`/demo/...`) |
| `wwwroot/index.html` | The demo page (plain JavaScript) |

Everything is in memory (orders, pending jobs, the feed) and starts empty on each run.

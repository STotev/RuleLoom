# Design: Samples for Demonstrating RuleLoom

**Issue:** [#158 Tidy up the samples](https://github.com/STotev/RuleLoom/issues/158)
**Builds on:** [the 0.8 sample](2026-11-17-sample-scheduling-design.md) (findings 3–6)
**Date:** 2026-11-17
**Status:** Implemented

---

## Problem

The samples are what a team sees first, often in a presentation, and they don't demo well:

- **The console sample** mixes three domains (car ads, invoices, orders) and four loosely related examples. Each builds
  its rule in C# object initialisers, which hide what the rule says.
- **The ASP.NET Core sample** asks the presenter to post a generic envelope (`eventType`, `tenantId`, `entityId`,
  `current`, `previous`) by hand. There are two tenants, two ids per order (findings 3 and 4), and the only way to see
  what happened is to read interleaved log lines.

## Decisions

Reviewed with the maintainer before building (HITL).

| Question | Decision |
|---|---|
| Domain | One web shop everywhere: orders, customers, payments. Car ads, invoices and tenants go |
| Console layout | One numbered, self-contained file per scenario in `Scenarios/`. Each reads top to bottom: a comment saying what it shows, the data, the rule (JSON in a raw string, as an author writes it), the catalog, the run, and the printed outcome. The shop's types are shared (`Shop.cs`) |
| Console scenarios | 1 First rule (also built in C#) · 2 Changes (`changed`, `$previous`) · 3 Contexts · 4 Typed actions and `$path` · 5 Validation · 6 Scheduling (Delay, Advance, calendar days, time zone) · 7 Re-checked when due (`ExecuteScheduledAsync`, a demo clock) · 8 Overdue actions (a timeline) · 9 Dependency injection · 10 The catalog document |
| Running the console | `dotnet run -- 4` runs one scenario; no argument lists them; `all` runs every scenario |
| Console output | Spectre.Console (MIT, sample only): each result as a tree (rule, then its actions with their status and timing), colours by status. The console sample doesn't log, so the output is only the scenario's story |
| Web sample shape | A small shop API: `POST /orders` (place), `POST /orders/{number}/pay`, `POST /orders/{number}/ship`, `GET /orders`. The app keeps its orders and raises `OrderPlaced`/`OrderUpdated` to RuleLoom **in-process**, with the previous state from its own store. `POST /events` goes |
| Ids | The order number is the only id: the subject events declare (`Subject(o => o.Number).LoadedBy("order")`), loaded from the app's own store (fixes findings 3 and 4) |
| Tenancy | One shop. "Storage and tenancy are yours" becomes a README paragraph |
| Rules | JSON files in `Rules/`, read for every event, so a rule edited during the demo applies to the next request. Rules with mistakes live in their own file, `Rules/mistakes.json`, and are listed (with their errors) on the page |
| Seeing what happened | A demo outbox (`GET /emails`) and an activity feed (`GET /activity`: each event and each scheduled run with its rules' and actions' outcomes), plus one static `wwwroot/index.html` (plain JS, no build): place, pay and ship orders with buttons; orders, emails, activity and rules with mistakes refresh every few seconds. The `.http` file stays |
| Demo timing | `POST /orders` takes `paymentDueInSeconds` (default 60); the reminder rule says "30 seconds before payment is due", so it fires about 30 seconds after placing an order |
| Logging (web) | The page shows the outcomes; the console keeps short Information lines (emails sent, scheduled runs) and warnings. RuleLoom's categories at Warning, Microsoft's and Quartz's at Warning. OpenTelemetry stays commented out, to uncomment |
| Tests | None: samples are checked by building and running them (the maintainer's call: tests for a sample are unnecessary code). #158's test criterion is dropped |
| Delivery | Two PRs, console first, then the web shop; titled `docs(samples): …` so they don't trigger a release |

## Console scenarios

| # | File | Shows |
|---|---|---|
| 1 | `01-FirstRule.cs` | An `OrderPlaced` rule ("big order": `Total gte 500`) with a `console-log` action, written as JSON and then the same rule in C# |
| 2 | `02-Changes.cs` | `OrderUpdated` with previous state: `changed Status`, and `$current.Total lt $previous.Total` (a discount) |
| 3 | `03-Contexts.cs` | `@customer.Tier eq gold`: the event declares the `customer` context, a provider loads it by the customer's email |
| 4 | `04-TypedActions.cs` | `send-email` with typed parameters; `to` given as `{ "$path": "@customer.Email" }` |
| 5 | `05-Validation.cs` | Rules with mistakes (an unknown field, an unknown operator, a misspelt and a missing parameter, an unknown event and action) rejected against the catalog, each error with its JSONPath |
| 6 | `06-Scheduling.cs` | A "thank you" email 1 hour after the order (Delay), a reminder 2 days before `PaymentDueAt` (Advance, calendar days in `Europe/Sofia`); RuleLoom returns `ScheduledAction`s instead of running them |
| 7 | `07-RecheckedWhenDue.cs` | The reminder is scheduled; the order is paid; at the due time `ExecuteScheduledAsync` loads the order as it is then and skips the reminder. An unpaid order's reminder is sent |
| 8 | `08-Overdue.cs` | `SkipOverdueActions` with a 30-second grace period: a reminder run on time, 15 seconds late (runs), and 45 seconds late (skipped), printed as a timeline |
| 9 | `09-DependencyInjection.cs` | `AddRuleLoom` with an action that injects the app's mailer |
| 10 | `10-TheCatalog.cs` | The catalog document an authoring UI reads: the event's fields and contexts, the actions and their parameters |
| 11 | `11-Observability.cs` | One event with four rules (one broken): the logs as written, the span tree with its attributes, and the metrics, caught with `ActivityListener`/`MeterListener` |

## Web shop layout

| Folder | Holds |
|---|---|
| `Program.cs` | Registration and endpoint mapping only |
| `Orders/` | `Order` and its event schemas, `OrderStore`, `OrderProvider` (the subject loader), the order endpoints |
| `Customers/` | The customer directory, the `customer` and `orderHistory` providers, the profile's schema |
| `Actions/` | `send-email` (writes to the outbox) and `loyalty-check` |
| `Rules/` | The rule files and `RuleStore` (read, parse, validate) |
| `Scheduling/` | `ActionScheduler` (Quartz job per scheduled action, JSON job data) and `RunScheduledActionJob` |
| `Demo/` | The outbox, the activity feed and their endpoints |
| `wwwroot/index.html` | The demo page |

## Observability (added after review)

The maintainer asked for a readable way to demo observability; the console exporter was unreadable.

| Question | Decision |
|---|---|
| Console | Scenario 11 listens in-process (`ActivityListener`, `MeterListener`, a printing `ILoggerFactory`) and prints the logs, the span tree and the metrics; no packages |
| Web shop | OTLP (`OpenTelemetry.Exporter.OpenTelemetryProtocol`, Apache-2.0, replacing the console exporter) to the standalone Aspire Dashboard (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.2`, Docker), enabled only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set: the `dashboard` launch profile sets it |
| Logs | Serilog passes its events on to OpenTelemetry (`writeToProviders`, with ASP.NET Core's default providers cleared so lines don't print twice). `RuleLoom.Engine` logs at Debug, the console sink only from Information: the outcomes go to the dashboard, not the console |

## Deviations

- **Console scenario order**: contexts and typed actions come before validation (3, 4, 5), so validation can show
  mistakes in context paths and action parameters. The catalog scenario's file is `10-TheCatalog.cs` (a class named
  `Catalog` would shadow RuleLoom's).
- **Where events meet RuleLoom**: `Rules/RuleRunner.cs` (load the rules, run them, schedule, record), so `Orders/` knows
  only the shop. The demo endpoints are under `/demo` (`/demo/activity`, `/demo/emails`, `/demo/rules`,
  `/demo/customers`).
- **Grace period of 10 seconds**, not a minute: with the reminder 30 seconds before payment is due, the page's "payment
  due in 25 / 15 seconds" choices show a late reminder sent at once (5 seconds late) and one skipped (15 seconds late).
- **Log levels**: only `RuleLoom.Engine` is set to Warning; a `RuleLoom` override would also silence the sample's own
  namespace (`RuleLoom.Samples...`). The catalog's startup line (Information) stays.

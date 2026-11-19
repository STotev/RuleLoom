# Console Sample

RuleLoom in a web shop, one scenario per file. Each file in [`Scenarios/`](Scenarios/) reads top to bottom: what it
shows, the rule (as JSON, the way an author writes it), the catalog, the run and what came out.

```bash
dotnet run --project samples/RuleLoom.Samples.Console          # lists the scenarios
dotnet run --project samples/RuleLoom.Samples.Console -- 7     # runs one
dotnet run --project samples/RuleLoom.Samples.Console -- all   # runs every scenario
```

| # | Scenario | Shows |
|---|---|---|
| 1 | [First rule](Scenarios/01-FirstRule.cs) | A rule on `OrderPlaced`: a condition and an action; the same rule built in C# |
| 2 | [Changes](Scenarios/02-Changes.cs) | `OrderUpdated` with the order before and after: `changed`, `$previous` |
| 3 | [Contexts](Scenarios/03-Contexts.cs) | `@customer.Tier`: data the event doesn't carry, loaded by a context provider when a rule needs it |
| 4 | [Typed actions](Scenarios/04-TypedActions.cs) | `send-email` with typed parameters, the recipient given as a path (`{ "$path": "@customer.Email" }`) |
| 5 | [Validation](Scenarios/05-Validation.cs) | Rules with mistakes rejected against the catalog, each problem with its JSONPath |
| 6 | [Scheduling](Scenarios/06-Scheduling.cs) | `Delay` and `Advance`; calendar days in the shop's time zone, across a daylight-saving change |
| 7 | [Re-checked when due](Scenarios/07-RecheckedWhenDue.cs) | A reminder scheduled, the order paid, and the reminder skipped when due: `ExecuteScheduledAsync` checks the order as it is then |
| 8 | [Overdue actions](Scenarios/08-Overdue.cs) | `SkipOverdueActions` and its grace period: on time, a little late, too late |
| 9 | [Dependency injection](Scenarios/09-DependencyInjection.cs) | `AddRuleLoom`, and an action that injects the app's services |
| 10 | [The catalog](Scenarios/10-TheCatalog.cs) | What a rule-authoring UI reads: events, fields, contexts, actions and their parameters |

The shop's types (orders, customers, the order events) are in [`Shop.cs`](Shop.cs). Scenarios 4 to 10 reuse the
`send-email` action and the customer context from scenarios 3 and 4. `DemoClock` moves time by hand, so scheduled
actions come due without waiting, and `Show` prints the output (with
[Spectre.Console](https://spectreconsole.net)); neither is part of using RuleLoom.

For an app over HTTP, with a scheduler, see the [ASP.NET Core sample](../RuleLoom.Samples.AspNetCore/).

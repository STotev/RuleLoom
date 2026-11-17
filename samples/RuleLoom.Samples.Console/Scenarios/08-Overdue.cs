using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// A scheduled action can run late: the scheduler was down, or the event arrived after the due time had passed. With
/// SkipOverdueActions, an action more than OverdueGracePeriod late is skipped instead of run late. The grace period
/// is about how late the run is, not how far ahead the action was scheduled.
/// </summary>
public static class Overdue
{
    private const string PaymentReminder = """
    {
      "id": "payment-reminder",
      "name": "Payment reminder",
      "eventType": "OrderPlaced",
      "condition": { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "AwaitingPayment" }] },
      "actions": [
        {
          "action": "send-email",
          "parameters": { "to": { "$path": "Customer.Email" }, "subject": "Your payment is due in an hour" },
          "schedule": { "mode": "Advance", "amount": 1, "unit": "Hours", "referencePath": "PaymentDueAt" }
        }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(8, "Overdue actions", "A reminder due at 09:00, run on time, 15 seconds late and 45 seconds late, with a 30-second grace period.");
        Show.Json("Rule", PaymentReminder);

        var clock = new DemoClock(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedSchema>()
            .AddAction<SendEmailAction>()                     // from scenario 4
            .Build();
        var options = new RuleEngineOptions { SkipOverdueActions = true, OverdueGracePeriod = TimeSpan.FromSeconds(30) };
        var engine = new RuleEngine(catalog, timeProvider: clock, options: options);
        var rules = new[] { RuleJsonParser.Parse(PaymentReminder) };

        var order = new Order("SO-1001", OrderStatus.AwaitingPayment, 640.50m, Customers.Ada, PaymentDueAt: clock.Now.AddHours(2));
        var placed = new RuleEvent("OrderPlaced", order);
        Show.Step($"{Show.Time(clock.Now)}: Ada places an order, payment due at {Show.Time(order.PaymentDueAt!.Value)}");
        var result = await engine.ExecuteAsync(placed, rules);
        Show.Result(result);
        var reminder = result.ScheduledActions[0];

        foreach (var late in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45) })
        {
            clock.Now = reminder.ScheduledFor + late;
            Show.Step($"{Show.Time(clock.Now)}: the scheduler runs the reminder, {late.TotalSeconds:0} seconds late");
            Show.Action(await engine.ExecuteScheduledAsync(reminder, placed, rules));
        }

        Show.End();
        Show.Step("An event that arrives late is treated the same way when it's processed");
        clock.Now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        Show.Note($"It's {Show.Time(clock.Now)}; payment is due in 15 minutes, so the reminder was due 45 minutes ago.");
        var lateOrder = order with { Number = "SO-1002", Customer = Customers.Bob, PaymentDueAt = clock.Now.AddMinutes(15) };
        Show.Result(await engine.ExecuteAsync(new RuleEvent("OrderPlaced", lateOrder), rules));
    }
}

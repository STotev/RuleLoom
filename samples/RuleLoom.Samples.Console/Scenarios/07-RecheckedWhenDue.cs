using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Contexts;
using RuleLoom.Engine;
using RuleLoom.Schemas;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// A reminder scheduled today may be wrong by the time it's due: the customer may have paid. The event's schema
/// declares its subject (the order, by number) and how to load it; when the reminder is due, ExecuteScheduledAsync
/// loads the order as it is then, checks the rule again, and runs the action or skips it with the reason.
/// </summary>
public static class RecheckedWhenDue
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
          "parameters": { "to": { "$path": "Customer.Email" }, "subject": "Your payment is due tomorrow" },
          "schedule": { "mode": "Advance", "amount": 1, "unit": "Days", "referencePath": "PaymentDueAt" }
        }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(7, "Re-checked when due", "Two unpaid orders get a reminder scheduled; Ada pays; when due, only Bob is reminded.");
        Show.Json("Rule", PaymentReminder);

        var clock = new DemoClock(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        var orders = new OrderDatabase();

        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedWithSubjectSchema>()
            .AddContextProvider<OrderLoader>("order")
            .AddAction<SendEmailAction>()                     // from scenario 4
            .Build();
        var engine = new RuleEngine(catalog, contextProviders: new DirectContextProviderFactory().Add(new OrderLoader(orders)), timeProvider: clock);
        var rules = new[] { RuleJsonParser.Parse(PaymentReminder) };

        // What the app's scheduler keeps for each scheduled action: the action and the event that scheduled it, as JSON.
        var pending = new List<(string ScheduledAction, string Event)>();

        foreach (var order in new[]
        {
            new Order("SO-1001", OrderStatus.AwaitingPayment, 640.50m, Customers.Ada, clock.Now.AddDays(7)),
            new Order("SO-1002", OrderStatus.AwaitingPayment, 75.00m, Customers.Bob, clock.Now.AddDays(7))
        })
        {
            Show.Step($"{Show.Time(clock.Now)}: {order.Customer.Name} places {order.Number}, payment due in 7 days");
            orders.Save(order);

            var placed = new RuleEvent("OrderPlaced", order);
            var result = await engine.ExecuteAsync(placed, rules);
            Show.Result(result);

            foreach (var scheduled in result.ScheduledActions)
            {
                Show.Note($"Scheduler: keep \"{scheduled.Key}\" until {Show.Time(scheduled.ScheduledFor)}");
                pending.Add((scheduled.ToJson(), placed.ToJson()));
            }
        }

        clock.Now = clock.Now.AddDays(2);
        Show.Step($"{Show.Time(clock.Now)}: Ada pays for SO-1001 (the app updates its database)");
        orders.Save(orders.Find("SO-1001")! with { Status = OrderStatus.Paid });
        Show.End();

        clock.Now = clock.Now.AddDays(4);
        Show.Step($"{Show.Time(clock.Now)}: the reminders are due; the scheduler runs each with one call");
        foreach (var (scheduledJson, eventJson) in pending)
        {
            var scheduled = ScheduledAction.Parse(scheduledJson);
            Show.Note($"Running \"{scheduled.Key}\"");
            Show.Action(await engine.ExecuteScheduledAsync(scheduled, RuleEvent.Parse(eventJson), rules));
        }

        Show.End();
    }
}

/// <summary>"OrderPlaced", whose subject is the order (by number), loaded by "order" when a scheduled action is due.</summary>
public sealed class OrderPlacedWithSubjectSchema : EventSchema<Order>
{
    public OrderPlacedWithSubjectSchema()
    {
        Event("OrderPlaced").Label("Order placed");
        Subject(o => o.Number).LoadedBy("order");
    }
}

/// <summary>The shop's orders as they are now; a real app has a database.</summary>
public sealed class OrderDatabase
{
    private readonly Dictionary<string, Order> _orders = [];

    public void Save(Order order) => _orders[order.Number] = order;

    public Order? Find(string number) => _orders.GetValueOrDefault(number);
}

/// <summary>Loads an order by number (the subject's identity) from the app's database.</summary>
public sealed class OrderLoader(OrderDatabase orders) : ContextProvider<Order>
{
    protected override ValueTask<Order?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(orders.Find(request.GetReference<string>()!));
}

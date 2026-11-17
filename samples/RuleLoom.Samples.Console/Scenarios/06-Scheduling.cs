using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
using Spectre.Console;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// An action can run later: "Delay" counts from now, "Advance" counts back from a date in the event. RuleLoom works out
/// when (ScheduledFor) and returns the scheduled actions instead of running them; your scheduler (Quartz, Hangfire...)
/// runs them when due (scenario 7). Days are calendar days in the catalog's time zone: the wall-clock time stays the
/// same across a daylight-saving change.
/// </summary>
public static class Scheduling
{
    private const string FollowUps = """
    {
      "id": "order-follow-ups",
      "name": "Order follow-ups",
      "eventType": "OrderPlaced",
      "condition": { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "AwaitingPayment" }] },
      "actions": [
        {
          "action": "send-email",
          "parameters": { "to": { "$path": "Customer.Email" }, "subject": "Thanks for your order" },
          "schedule": { "mode": "Delay", "amount": 1, "unit": "Hours" }
        },
        {
          "action": "send-email",
          "parameters": { "to": { "$path": "Customer.Email" }, "subject": "Your payment is due in 2 days" },
          "schedule": { "mode": "Advance", "amount": 2, "unit": "Days", "referencePath": "PaymentDueAt" }
        }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(6, "Scheduling", "A thank-you email an hour after the order, and a reminder 2 days before payment is due.");
        Show.Json("Rule", FollowUps);

        var sofia = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia");
        var clock = new DemoClock(new DateTimeOffset(2026, 3, 27, 10, 0, 0, TimeSpan.FromHours(2)));   // Friday, 10:00 in Sofia

        var catalog = new CatalogBuilder()
            .UseTimeZone("Europe/Sofia")                      // the shop's time zone; UTC when not set
            .AddEvent<OrderPlacedSchema>()
            .AddAction<SendEmailAction>()
            .Build();
        var engine = new RuleEngine(catalog, timeProvider: clock);

        // Payment is due Monday at 09:00. The clocks go forward on Sunday 29 March, so Monday is UTC+3 and Saturday UTC+2.
        var dueAt = new DateTimeOffset(2026, 3, 30, 9, 0, 0, TimeSpan.FromHours(3));
        Show.Step($"Ada places an order on {Local(clock.Now, sofia)}; payment is due {Local(dueAt, sofia)}");
        var order = new Order("SO-1001", OrderStatus.AwaitingPayment, 640.50m, Customers.Ada, dueAt);
        var result = await engine.ExecuteAsync("OrderPlaced", order, [RuleJsonParser.Parse(FollowUps)]);
        Show.Result(result);

        Show.Step("Hand these to your scheduler:");
        var table = new Table().AddColumns("Rule", "Action", "Run at (UTC)", "Run at (Sofia)");
        foreach (ScheduledAction scheduled in result.ScheduledActions)
        {
            table.AddRow(scheduled.RuleId, $"#{scheduled.ActionIndex} {scheduled.ActionName}", Show.Time(scheduled.ScheduledFor),
                Local(scheduled.ScheduledFor, sofia));
        }

        AnsiConsole.Write(table);
        Show.Note("The reminder is at 09:00 Sofia time two calendar days earlier, although the UTC offset changed in between.");
        Show.End();
    }

    private static string Local(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString("ddd d MMM HH:mm (UTCzzz)");
}

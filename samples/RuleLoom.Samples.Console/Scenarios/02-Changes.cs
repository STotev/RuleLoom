using RuleLoom.Abstractions.Rule;
using RuleLoom.Actions;
using RuleLoom.Engine;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// An update event carries the order before and after. Rules compare them: "changed" asks whether a field changed,
/// "$previous.X" reads the old value ("X" and "$current.X" read the new one).
/// </summary>
public static class Changes
{
    private const string Rules = """
    [
      {
        "id": "order-paid",
        "name": "Order paid",
        "eventType": "OrderUpdated",
        "condition": {
          "operator": "and",
          "operands": [
            { "operator": "changed", "operands": [{ "path": "Status" }] },
            { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Paid" }] }
          ]
        },
        "actions": [{ "action": "console-log", "parameters": { "message": "Paid: prepare the parcel" } }]
      },
      {
        "id": "discount-applied",
        "name": "Discount applied",
        "eventType": "OrderUpdated",
        "condition": { "operator": "lt", "operands": [{ "path": "$current.Total" }, { "path": "$previous.Total" }] },
        "actions": [{ "action": "console-log", "parameters": { "message": "The total went down: tell finance" } }]
      },
      {
        "id": "order-shipped",
        "name": "Order shipped",
        "eventType": "OrderUpdated",
        "condition": { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Shipped" }] },
        "actions": [{ "action": "console-log", "parameters": { "message": "Shipped: send the tracking link" } }]
      }
    ]
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(2, "Changes", "Rules on OrderUpdated compare the order before and after the change.");
        Show.Json("Rules", Rules);

        var catalog = new CatalogBuilder()
            .AddEvent<OrderUpdatedSchema>()
            .AddAction<ConsoleLogAction>()
            .Build();
        var engine = new RuleEngine(catalog);
        var rules = RuleJsonParser.ParseMany(Rules);

        Show.Step("Ada pays for her order, and a 10% discount is applied");
        var before = new Order("SO-1001", OrderStatus.AwaitingPayment, 640.50m, Customers.Ada);
        var after = before with { Status = OrderStatus.Paid, Total = 576.45m };
        Show.Result(await engine.ExecuteAsync("OrderUpdated", after, before, rules));
    }
}

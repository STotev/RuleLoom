using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Actions;
using RuleLoom.Engine;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// A rule is an event type, a condition and actions. The engine runs the rules for an event and reports, per rule,
/// whether it matched and what its actions did.
/// </summary>
public static class FirstRule
{
    // Rules are usually written as JSON (by people, in a UI) and stored by the app.
    private const string BigOrder = """
    {
      "id": "big-order",
      "name": "Big order",
      "eventType": "OrderPlaced",
      "condition": { "operator": "gte", "operands": [{ "path": "Total" }, { "value": 500 }] },
      "actions": [
        { "action": "console-log", "parameters": { "message": "Big order: offer free shipping" } }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(1, "First rule", "A rule on OrderPlaced: when the total is at least 500, log a message.");
        Show.Json("Rule", BigOrder);

        // The catalog is the vocabulary rules are written in: events, operators, actions. console-log is built in.
        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedSchema>()
            .AddAction<ConsoleLogAction>()
            .Build();
        var engine = new RuleEngine(catalog);
        var rule = RuleJsonParser.Parse(BigOrder);

        Show.Step("Ada places an order of 640.50");
        var adasOrder = new Order("SO-1001", OrderStatus.AwaitingPayment, 640.50m, Customers.Ada);
        Show.Result(await engine.ExecuteAsync("OrderPlaced", adasOrder, [rule]));

        Show.Step("Bob places an order of 75.00; this time the same rule is built in C#");
        var sameRuleInCode = new RuleDefinition
        {
            Id = "big-order",
            Name = "Big order",
            EventType = "OrderPlaced",
            Condition = new OperatorCondition
            {
                Operator = "gte",
                Operands = [new PathOperand { Path = "Total" }, new ValueOperand { Value = 500 }]
            },
            Actions = [new ActionDefinition { Action = "console-log", Parameters = { ["message"] = "Big order: offer free shipping" } }]
        };
        var bobsOrder = new Order("SO-1002", OrderStatus.AwaitingPayment, 75.00m, Customers.Bob);
        Show.Result(await engine.ExecuteAsync("OrderPlaced", bobsOrder, [sameRuleInCode]));
    }
}

using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Actions;
using RuleLoom.Engine;

namespace RuleLoom.Samples.Console;

/// <summary>
/// A rule built in code, run without dependency injection.
/// </summary>
internal static class BasicExample
{
    public static async Task RunAsync()
    {
        var rule = new RuleDefinition
        {
            Id = "price-drop",
            Name = "Price drop",
            EventType = "CarAdUpdated",
            // The price changed and is now below the previous price.
            Condition = new OperatorCondition
            {
                Operator = "and",
                Operands =
                [
                    new OperatorCondition { Operator = "changed", Operands = [new PathOperand { Path = "Price" }] },
                    new OperatorCondition
                    {
                        Operator = "lt",
                        Operands = [new PathOperand { Path = "$current.Price" }, new PathOperand { Path = "$previous.Price" }]
                    }
                ]
            },
            Actions =
            [
                new ActionDefinition { Action = "console-log", Parameters = { ["message"] = "Price dropped — notify watchers." } },
                new ActionDefinition
                {
                    Action = "console-log",
                    Parameters = { ["message"] = "Big drop — flag for review." },
                    // Only when the new price is more than 10% below the market value.
                    Condition = new OperatorCondition
                    {
                        Operator = "lt",
                        Operands = [new PathOperand { Path = "Price" }, new ValueOperand { Value = 20_250 }]
                    }
                }
            ]
        };

        var catalog = new CatalogBuilder().AddAction<ConsoleLogAction>().Build();
        var engine = new RuleEngine(catalog);

        var previous = new CarAd { Title = "2019 Volvo V60", Price = 23_500m, MarketValue = 22_500m, Status = AdStatus.Active };
        var current = previous with { Price = 21_500m };

        var result = await engine.ExecuteAsync("CarAdUpdated", current, previous, [rule]);

        ResultPrinter.Print("Basic: a rule in code", result);
    }
}

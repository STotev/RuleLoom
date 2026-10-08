using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Actions;
using RuleForge.Engine;
using RuleForge.Evaluators;
using RuleForge.Operators;

namespace RuleForge.Samples.Console;

/// <summary>
/// A rule built in code, run without dependency injection.
/// </summary>
internal static class BasicExample
{
    public static async Task RunAsync()
    {
        var rule = new RuleDefinition
        {
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
                new ActionDefinition { Action = "log", Parameters = { ["message"] = "Price dropped — notify watchers." } },
                new ActionDefinition
                {
                    Action = "log",
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

        var engine = new RuleEngine(
            new ConditionEvaluator(BuiltInOperators.Create()),
            new DirectActionFactory([new LogAction()]));

        var previous = new CarAd { Title = "2019 Volvo V60", Price = 23_500m, MarketValue = 22_500m, Status = AdStatus.Active };
        var current = previous with { Price = 21_500m };

        var result = await engine.ExecuteAsync("CarAdUpdated", current, previous, [rule]);

        ResultPrinter.Print("Basic: a rule in code", result);
    }
}

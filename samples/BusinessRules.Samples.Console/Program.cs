// See https://aka.ms/new-console-template for more information

using BusinessRules.Abstractions;
using BusinessRules.Core.Actions;
using BusinessRules.Core.Engine;
using BusinessRules.Core.Operators;
using BusinessRules.Samples.Console;

var rules = new List<RuleDefinition>
{
    new()
    {
        Name = "Price changed",
        EventName = "PropertyUpdated",
        Priority = 1,
        Condition = new ConditionDefinition
        {
            Operator = "changed",
            Args = ["Price"]
        },
        Actions =
        [
            new ActionDefinition
            {
                Type = "log",
                Condition = new ConditionDefinition
                {
                    Operator = "eq",
                    Args = ["Price", 120]
                }
            }
        ]
    }
};

var engine = new RuleEngine(
    rules,
    [new ChangedOperator(), new EqualsOperator()],
    new Dictionary<string, IAction>
    {
        ["log"] = new LogAction()
    });

await engine.ProcessAsync(new EventContext
{
    EventName = "PropertyUpdated",
    Current = new Property { Price = 120 },
    Previous = new Property { Price = 121 }
});
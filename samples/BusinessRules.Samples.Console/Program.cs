using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Action;
using BusinessRules.Abstractions.Condition;
using BusinessRules.Abstractions.Rule;
using BusinessRules.Core.Actions;
using BusinessRules.Core.Engine;
using BusinessRules.Core.Evaluators;
using BusinessRules.Core.Operators;
using BusinessRules.Samples.Console;

var rule = new RuleDefinition
{
    Name = "High price alert",
    EventType = "PropertyUpdated",
    Condition = new OperatorCondition
    {
        Operator = "eq",
        Operands =
        [
            new PathOperand { Path = "Price" },
            new PathOperand { Path = "EstimatedPrice" }
        ]
    },
    Actions =
    [
        new ActionDefinition
        {
            Type = "log",
            Parameters = { ["message"] = "Price is high!" },
            Condition = new OperatorCondition
            {
                Operator = "not",
                Operands =
                [
                    new OperatorCondition
                    {
                        Operator = "eq",
                        Operands =
                        [
                            new PathOperand { Path = "Price" },
                            new ValueOperand { Value = 100 }
                        ]
                    }
                ]
            }
        }
    ]
};

var rule2 = new RuleDefinition
{
    Name = "Price changed alert",
    EventType = "PropertyUpdated",
    Condition = new OperatorCondition
    {
        Operator = "changed",
        Operands =
        {
            new PathOperand { Path = "Price" }
        }
    },
    Actions =
    {
        new ActionDefinition
        {
            Type = "log",
            Parameters = { ["message"] = "Price has changed!" }
        }
    }
};

var conditions = new List<IOperator>
{
    new AndOperator(), new EqualsOperator(), new NotOperator(), new ChangedOperator(), new OrOperator()
};

var actions = new List<IActionExecutor> { new LogAction() };

var engine = new RuleEngine(new ExpressionEvaluator(conditions), actions);

var previousProperty = new Property
{
    Price = 100000,
    EstimatedPrice = 100000
};

var currentProperty = new Property
{
    Price = 150000,
    EstimatedPrice = 1500001
};

// await engine.ExecuteAsync("PropertyUpdated", currentProperty, previousProperty, [rul2]);
await engine.ExecuteAsync("PropertyUpdated", currentProperty, [rule]);

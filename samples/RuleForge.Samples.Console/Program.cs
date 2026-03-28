using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Actions;
using RuleForge.Engine;
using RuleForge.Evaluators;
using RuleForge.Operators;
using RuleForge.Samples.Console;

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
                            new ValueOperand { Value = 1500001 }
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

var json = """
{
  "name": "High price alert",
  "eventType": "PropertyUpdated",
  "condition": {
    "operator": "eq",
    "operands": [
      { "path": "Price" },
      { "path": "EstimatedPrice" }
    ]
  },
  "actions": [
    {
      "type": "log",
      "parameters": { "message": "Price is high!" },
      "condition": {
        "operator": "not",
        "operands": [
          {
            "operator": "eq",
            "operands": [
              { "path": "Price" },
              { "value": 150000 }
            ]
          }
        ]
      }
    }
  ]
}
""";

var rule3 = RuleJsonParser.Parse(json);

var conditions = new List<IConditionOperator>
{
    new AndOperator(), new EqualsOperator(), new NotOperator(), new ChangedOperator(), new OrOperator()
};

var actions = new List<IActionExecutor> { new LogAction() };

var engine = new RuleEngine(new ConditionEvaluator(conditions), actions);

var previousProperty = new Property
{
    Price = 100000,
    EstimatedPrice = 100000
};

var currentProperty = new Property
{
    Price = 100000,
    EstimatedPrice = 100000
};

// await engine.ExecuteAsync("PropertyUpdated", currentProperty, previousProperty, [rule2]);
// await engine.ExecuteAsync("PropertyUpdated", currentProperty, [rule]);
await engine.ExecuteAsync("PropertyUpdated", currentProperty, [rule3]);

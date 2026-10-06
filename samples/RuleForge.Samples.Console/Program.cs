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
            Action = "log",
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
            Action = "log",
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
      "action": "log",
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
    },
    {
      "action": "invalid.log",
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
    },
    {
      "action": "log",
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
      },
      "schedule": {
        "mode": "delay",
        "amount": 30,
        "unit": "minutes",
        "referencePath": "UpdatedAt"
      }
    }
  ]
}
""";

var rule3 = RuleJsonParser.Parse(json);

var conditions = new List<IConditionOperator>
{
    new AndOperator(), new EqualsOperator(), new NotOperator(), new ChangedOperator(), new OrOperator(),
    new GreaterThanOperator(), new GreaterThanOrEqualOperator(), new LessThanOperator(), new LessThanOrEqualOperator()
};

var actions = new List<IAction> { new LogAction() };

var actionFactory = new DirectActionFactory(actions);
var engine = new RuleEngine(new ConditionEvaluator(conditions), actionFactory);

var previousProperty = new Property
{
    Price = 100000,
    EstimatedPrice = 100000
};

var currentProperty = new Property
{
    Price = 100000,
    EstimatedPrice = 100000,
    // UpdatedAt = DateTimeOffset.Parse("2014-02-02T00:00:00.000Z")
    // UpdatedAt = "2014-02-02T00:00:00"
    UpdatedAt = "asd"
};

// await engine.ExecuteAsync("PropertyUpdated", currentProperty, previousProperty, [rule2]);
// await engine.ExecuteAsync("PropertyUpdated", currentProperty, [rule]);
var result = await engine.ExecuteAsync("PropertyUpdated", currentProperty, [rule3]);

foreach (var ruleResult in result.Rules)
{
    Console.WriteLine($"[{ruleResult.RuleName}] {ruleResult.Status}{(ruleResult.Error is null ? "" : $": {ruleResult.Error.Message}")}");

    foreach (var actionResult in ruleResult.ActionResults)
    {
        Console.WriteLine($"  #{actionResult.ActionIndex} '{actionResult.ActionName}' {actionResult.Status}: {actionResult.Message}");
    }
}

foreach (var scheduled in result.ScheduledActions)
{
    Console.WriteLine($"Scheduled '{scheduled.Action.Action}' from [{scheduled.RuleName}] for {scheduled.Result.ScheduledFor:O}");
}

Console.WriteLine($"Has failures: {result.HasFailures}");

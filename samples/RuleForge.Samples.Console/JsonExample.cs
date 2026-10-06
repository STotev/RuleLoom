using RuleForge.Abstractions.Rule;
using RuleForge.Actions;
using RuleForge.Engine;
using RuleForge.Evaluators;
using RuleForge.Operators;
using RuleForge.Validation;

namespace RuleForge.Samples.Console;

/// <summary>
/// Rules authored as JSON: parsed, validated against the registered operators and actions, then run.
/// </summary>
internal static class JsonExample
{
    private const string Rules = """
    [
      {
        "name": "Listed below estimate",
        "eventType": "PropertyListed",
        "priority": 10,
        "condition": {
          "operator": "and",
          "operands": [
            { "operator": "eq", "operands": [{ "path": "status" }, { "value": "active" }] },
            { "operator": "lt", "operands": [{ "path": "Price" }, { "path": "EstimatedPrice" }] }
          ]
        },
        "actions": [
          { "action": "log", "parameters": { "message": "Bargain listed!" } }
        ]
      },
      {
        "name": "Typo in operator",
        "eventType": "PropertyListed",
        "condition": { "operator": "greater", "operands": [{ "path": "Price" }, { "value": 1000000 }] },
        "actions": [{ "action": "send-sms" }]
      }
    ]
    """;

    public static async Task RunAsync()
    {
        var operators = BuiltInOperators.Create();
        var actions = new DirectActionFactory([new LogAction()]);
        var validator = new RuleValidator(operators, actions);

        var rules = RuleJsonParser.ParseMany(Rules);

        // Reject invalid rules before running (or saving) them.
        var valid = new List<RuleDefinition>();
        foreach (var rule in rules)
        {
            var validation = validator.Validate(rule);
            if (validation.IsValid)
            {
                valid.Add(rule);
                continue;
            }

            System.Console.WriteLine($"Rejected '{rule.Name}':");
            foreach (var error in validation.Errors)
                System.Console.WriteLine($"  {error.Path}: {error.Message}");
        }

        var engine = new RuleEngine(new ConditionEvaluator(operators), actions);
        var property = new Property { Address = "2 Oak Road", Price = 390_000m, EstimatedPrice = 420_000m, Status = ListingStatus.Active };

        var result = await engine.ExecuteAsync("PropertyListed", property, valid);

        ResultPrinter.Print("JSON: parse, validate, run", result);
    }
}

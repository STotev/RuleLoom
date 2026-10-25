using RuleLoom.Abstractions.Rule;
using RuleLoom.Actions;
using RuleLoom.Engine;
using RuleLoom.Validation;

namespace RuleLoom.Samples.Console;

/// <summary>
/// Rules authored as JSON: parsed, validated against the catalog (events, operators, actions), then run.
/// </summary>
internal static class JsonExample
{
    private const string Rules = """
    [
      {
        "id": "below-market-value",
        "name": "Below market value",
        "eventType": "CarAdPublished",
        "priority": 10,
        "condition": {
          "operator": "and",
          "operands": [
            { "operator": "eq", "operands": [{ "path": "status" }, { "value": "active" }] },
            { "operator": "lt", "operands": [{ "path": "Price" }, { "path": "MarketValue" }] }
          ]
        },
        "actions": [
          { "action": "console-log", "parameters": { "message": "Bargain published!" } }
        ]
      },
      {
        "id": "typo-in-operator",
        "name": "Typo in operator",
        "eventType": "CarAdPublished",
        "condition": { "operator": "greater", "operands": [{ "path": "Price" }, { "value": 50000 }] },
        "actions": [{ "action": "send-sms" }]
      }
    ]
    """;

    public static async Task RunAsync()
    {
        var catalog = new CatalogBuilder().AddEvent<CarAdPublishedSchema>().AddAction<ConsoleLogAction>().Build();
        var validator = new RuleValidator(catalog);

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

        var engine = new RuleEngine(catalog);
        var ad = new CarAd { Title = "2020 Ford Focus", Price = 13_900m, MarketValue = 15_200m, Status = AdStatus.Active };

        var result = await engine.ExecuteAsync("CarAdPublished", ad, valid);

        ResultPrinter.Print("JSON: parse, validate, run", result);
    }
}

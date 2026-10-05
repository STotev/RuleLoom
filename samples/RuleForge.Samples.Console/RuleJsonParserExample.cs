using RuleForge.Abstractions.Rule;

namespace RuleForge.Samples.Console;

/// <summary>
/// Example demonstrating the performant RuleJsonParser for parsing JSON rules.
/// </summary>
public static class RuleJsonParserExample
{
    public static void RunExample()
    {
        System.Console.WriteLine("=== JSON Rule Parser Example ===\n");

        // Example 1: Simple rule from JSON string
        var simpleJson = """
        {
          "name": "Price change detection",
          "eventType": "PropertyUpdated",
          "priority": 1,
          "condition": {
            "operator": "changed",
            "operands": [
              { "path": "Price" }
            ]
          },
          "actions": [
            {
              "type": "log",
              "parameters": { "message": "Price has changed!" }
            }
          ]
        }
        """;

        try
        {
            var rule1 = RuleJsonParser.Parse(simpleJson);
            System.Console.WriteLine($"✓ Parsed rule: {rule1.Name}");
            System.Console.WriteLine($"  Event Type: {rule1.EventType}");
            System.Console.WriteLine($"  Priority: {rule1.Priority}");
            System.Console.WriteLine($"  Condition Type: {rule1.Condition.GetType().Name}");
            System.Console.WriteLine($"  Actions: {rule1.Actions.Count}\n");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"✗ Failed to parse rule: {ex.Message}\n");
        }

        // Example 2: Complex rule with nested conditions
        var complexJson = """
        {
          "name": "High price alert",
          "eventType": "PropertyUpdated",
          "priority": 2,
          "condition": {
            "operator": "and",
            "operands": [
              {
                "operator": "changed",
                "operands": [
                  { "path": "Price" }
                ]
              },
              {
                "operator": "not",
                "operands": [
                  {
                    "operator": "eq",
                    "operands": [
                      { "path": "Price" },
                      { "value": 1500001 }
                    ]
                  }
                ]
              }
            ]
          },
          "actions": [
            {
              "type": "log",
              "parameters": { "message": "High price detected!", "severity": "warning" },
              "condition": {
                "operator": "or",
                "operands": [
                  {
                    "operator": "eq",
                    "operands": [
                      { "path": "Price" },
                      { "value": 100000 }
                    ]
                  },
                  {
                    "operator": "eq",
                    "operands": [
                      { "path": "Price" },
                      { "value": 200000 }
                    ]
                  }
                ]
              }
            }
          ]
        }
        """;

        try
        {
            var rule2 = RuleJsonParser.Parse(complexJson);
            System.Console.WriteLine($"✓ Parsed complex rule: {rule2.Name}");
            System.Console.WriteLine($"  Event Type: {rule2.EventType}");
            System.Console.WriteLine($"  Condition Type: {rule2.Condition.GetType().Name}");
            System.Console.WriteLine($"  Actions: {rule2.Actions.Count}");
            System.Console.WriteLine($"  First action condition: {rule2.Actions[0].Condition?.GetType().Name}\n");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"✗ Failed to parse complex rule: {ex.Message}\n");
        }

        // Example 3: Batch parsing multiple rules
        var batchJson = """
        [
          {
            "name": "Rule 1",
            "eventType": "Event1",
            "condition": { "operator": "changed", "operands": [{ "path": "Field1" }] }
          },
          {
            "name": "Rule 2",
            "eventType": "Event2",
            "condition": { "operator": "eq", "operands": [{ "path": "Field2" }, { "value": 42 }] }
          }
        ]
        """;

        try
        {
            var rules = RuleJsonParser.ParseMany(batchJson);
            System.Console.WriteLine($"✓ Parsed {rules.Count} rules from batch");
            foreach (var rule in rules)
            {
                System.Console.WriteLine($"  - {rule.Name} ({rule.EventType})");
            }
            System.Console.WriteLine();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"✗ Failed to parse batch: {ex.Message}\n");
        }
    }
}


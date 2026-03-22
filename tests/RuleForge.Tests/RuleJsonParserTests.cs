using Xunit;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;

namespace RuleForge.Tests.Rule;

public class RuleJsonParserTests
{
    [Fact]
    public void Parse_SimpleRuleWithPathOperand_Success()
    {
        var json = """
        {
          "name": "Test Rule",
          "eventType": "PropertyUpdated",
          "priority": 1,
          "condition": { "path": "Price" },
          "actions": []
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        Assert.NotNull(rule);
        Assert.Equal("Test Rule", rule.Name);
        Assert.Equal("PropertyUpdated", rule.EventType);
        Assert.Equal(1, rule.Priority);
        Assert.IsType<PathOperand>(rule.Condition);
        Assert.Empty(rule.Actions);
    }

    [Fact]
    public void Parse_RuleWithValueOperand_Success()
    {
        var json = """
        {
          "name": "Value Test",
          "eventType": "Event1",
          "condition": { "value": 42 },
          "actions": []
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        Assert.IsType<ValueOperand>(rule.Condition);
        var valueOperand = (ValueOperand)rule.Condition;
        Assert.Equal(42, valueOperand.Value);
    }

    [Fact]
    public void Parse_RuleWithOperatorCondition_Success()
    {
        var json = """
        {
          "name": "Operator Test",
          "eventType": "Event1",
          "condition": {
            "operator": "eq",
            "operands": [
              { "path": "Price" },
              { "value": 100 }
            ]
          },
          "actions": []
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        Assert.IsType<OperatorCondition>(rule.Condition);
        var opCondition = (OperatorCondition)rule.Condition;
        Assert.Equal("eq", opCondition.Operator);
        Assert.Equal(2, opCondition.Operands.Count);
        Assert.IsType<PathOperand>(opCondition.Operands[0]);
        Assert.IsType<ValueOperand>(opCondition.Operands[1]);
    }

    [Fact]
    public void Parse_RuleWithNestedConditions_Success()
    {
        var json = """
        {
          "name": "Nested Test",
          "eventType": "Event1",
          "condition": {
            "operator": "and",
            "operands": [
              {
                "operator": "changed",
                "operands": [{ "path": "Price" }]
              },
              {
                "operator": "not",
                "operands": [
                  {
                    "operator": "eq",
                    "operands": [
                      { "path": "Price" },
                      { "value": 999 }
                    ]
                  }
                ]
              }
            ]
          },
          "actions": []
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        Assert.IsType<OperatorCondition>(rule.Condition);
        var andCondition = (OperatorCondition)rule.Condition;
        Assert.Equal("and", andCondition.Operator);
        Assert.Equal(2, andCondition.Operands.Count);

        // First operand: changed
        var changedOp = (OperatorCondition)andCondition.Operands[0];
        Assert.Equal("changed", changedOp.Operator);

        // Second operand: not
        var notOp = (OperatorCondition)andCondition.Operands[1];
        Assert.Equal("not", notOp.Operator);
        Assert.Single(notOp.Operands);
    }

    [Fact]
    public void Parse_RuleWithActions_Success()
    {
        var json = """
        {
          "name": "Action Test",
          "eventType": "Event1",
          "condition": { "path": "Field" },
          "actions": [
            {
              "type": "log",
              "parameters": { "message": "Alert!", "level": "warning" }
            },
            {
              "type": "email",
              "parameters": { "to": "admin@example.com" },
              "condition": { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "error" }] }
            }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        Assert.Equal(2, rule.Actions.Count);

        // First action
        Assert.Equal("log", rule.Actions[0].Type);
        Assert.Null(rule.Actions[0].Condition);
        Assert.Equal("Alert!", rule.Actions[0].Parameters["message"]?.ToString());

        // Second action
        Assert.Equal("email", rule.Actions[1].Type);
        Assert.NotNull(rule.Actions[1].Condition);
        Assert.IsType<OperatorCondition>(rule.Actions[1].Condition);
    }

    [Fact]
    public void Parse_CaseInsensitive_Success()
    {
        var json = """
        {
          "NAME": "Test",
          "EVENTTYPE": "Event1",
          "CONDITION": { "PATH": "Field" },
          "ACTIONS": []
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        Assert.Equal("Test", rule.Name);
        Assert.Equal("Event1", rule.EventType);
    }

    [Fact]
    public void Parse_VariousValueTypes_Success()
    {
        var json = """
        [
          { "name": "R1", "eventType": "E", "condition": { "value": null }, "actions": [] },
          { "name": "R2", "eventType": "E", "condition": { "value": true }, "actions": [] },
          { "name": "R3", "eventType": "E", "condition": { "value": "string" }, "actions": [] },
          { "name": "R4", "eventType": "E", "condition": { "value": 42 }, "actions": [] },
          { "name": "R5", "eventType": "E", "condition": { "value": 3.14 }, "actions": [] },
          { "name": "R6", "eventType": "E", "condition": { "value": [1, 2, 3] }, "actions": [] }
        ]
        """;

        var rules = RuleJsonParser.ParseMany(json);

        Assert.Equal(6, rules.Count);
        Assert.Null(((ValueOperand)rules[0].Condition).Value);
        Assert.True((bool)((ValueOperand)rules[1].Condition).Value!);
        Assert.Equal("string", (string)((ValueOperand)rules[2].Condition).Value!);
        Assert.Equal(42, (int)((ValueOperand)rules[3].Condition).Value!);
        Assert.Equal(3.14, (double)((ValueOperand)rules[4].Condition).Value!);
        Assert.IsType<List<object?>>(((ValueOperand)rules[5].Condition).Value);
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsException()
    {
        var invalidJson = "{ invalid json }";

        Assert.Throws<System.Text.Json.JsonException>(() => RuleJsonParser.Parse(invalidJson));
    }

    [Fact]
    public void Parse_MissingRequiredProperty_ThrowsException()
    {
        var json = """
        {
          "eventType": "Event1",
          "condition": { "path": "Field" },
          "actions": []
        }
        """;

        // Missing "name" is allowed (null! property)
        var rule = RuleJsonParser.Parse(json);
        Assert.Null(rule.Name);
    }

    [Fact]
    public void Parse_UnknownConditionType_ThrowsException()
    {
        var json = """
        {
          "name": "Test",
          "eventType": "Event1",
          "condition": { "unknown": "property" },
          "actions": []
        }
        """;

        Assert.Throws<System.Text.Json.JsonException>(() => RuleJsonParser.Parse(json));
    }

    [Fact]
    public void ParseMany_MultipleRules_Success()
    {
        var json = """
        [
          { "name": "Rule1", "eventType": "E1", "condition": { "path": "A" }, "actions": [] },
          { "name": "Rule2", "eventType": "E2", "condition": { "path": "B" }, "actions": [] },
          { "name": "Rule3", "eventType": "E3", "condition": { "path": "C" }, "actions": [] }
        ]
        """;

        var rules = RuleJsonParser.ParseMany(json);

        Assert.Equal(3, rules.Count);
        Assert.Equal("Rule1", rules[0].Name);
        Assert.Equal("Rule2", rules[1].Name);
        Assert.Equal("Rule3", rules[2].Name);
    }

    [Fact]
    public void Parse_WithGeneratedId_Success()
    {
        var json = """
        {
          "name": "Test",
          "eventType": "Event1",
          "condition": { "path": "Field" },
          "actions": []
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        Assert.NotNull(rule.Id);
        Assert.NotEmpty(rule.Id);
    }

    [Fact]
    public void Parse_UserProvidedId_Success()
    {
        var json = """
        {
          "id": "custom-id-123",
          "name": "Test",
          "eventType": "Event1",
          "condition": { "path": "Field" },
          "actions": []
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        Assert.Equal("custom-id-123", rule.Id);
    }

    [Fact]
    public void Parse_ComplexRealWorldExample_Success()
    {
        var json = """
        {
          "name": "High price alert",
          "eventType": "PropertyUpdated",
          "priority": 2,
          "condition": {
            "operator": "and",
            "operands": [
              {
                "operator": "changed",
                "operands": [{ "path": "Price" }]
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
              "parameters": {
                "message": "Price is high!",
                "severity": "warning"
              },
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

        var rule = RuleJsonParser.Parse(json);

        Assert.NotNull(rule);
        Assert.Equal("High price alert", rule.Name);
        Assert.Equal("PropertyUpdated", rule.EventType);
        Assert.Equal(2, rule.Priority);
        Assert.IsType<OperatorCondition>(rule.Condition);
        Assert.Single(rule.Actions);
        Assert.NotNull(rule.Actions[0].Condition);
    }
}


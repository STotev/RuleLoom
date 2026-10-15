using System.Text.Json;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;

namespace RuleLoom.Tests;

public class RuleJsonParserTests
{
    [Fact]
    public void Parse_SimpleRuleWithPathOperand_Success()
    {
        const string json =
            """
            {
              "id": "test-rule",
              "name": "Test Rule",
              "eventType": "OrderUpdated",
              "priority": 1,
              "condition": { "operator": "changed", "operands": [{ "path": "Price" }] },
              "actions": []
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        Assert.NotNull(rule);
        Assert.Equal("Test Rule", rule.Name);
        Assert.Equal("OrderUpdated", rule.EventType);
        Assert.Equal(1, rule.Priority);
        var condition = Assert.IsType<OperatorCondition>(rule.Condition);
        Assert.Equal("Price", Assert.IsType<PathOperand>(Assert.Single(condition.Operands)).Path);
        Assert.Empty(rule.Actions);
    }

    [Fact]
    public void Parse_RuleWithValueOperand_Success()
    {
        const string json =
            """
            {
              "id": "value-test",
              "name": "Value Test",
              "eventType": "Event1",
              "condition": { "operator": "eq", "operands": [{ "path": "Answer" }, { "value": 42 }] },
              "actions": []
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        var condition = Assert.IsType<OperatorCondition>(rule.Condition);
        var valueOperand = Assert.IsType<ValueOperand>(condition.Operands[1]);
        Assert.Equal(42, valueOperand.Value);
    }

    [Fact]
    public void Parse_RuleWithOperatorCondition_Success()
    {
        const string json =
            """
            {
              "id": "operator-test",
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
        const string json =
            """
            {
              "id": "nested-test",
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
        const string json =
            """
            {
              "id": "action-test",
              "name": "Action Test",
              "eventType": "Event1",
              "condition": { "operator": "changed", "operands": [{ "path": "Field" }] },
              "actions": [
                {
                  "action": "log",
                  "parameters": { "message": "Alert!", "level": "warning" }
                },
                {
                  "action": "email",
                  "parameters": { "to": "admin@example.com" },
                  "condition": { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "error" }] }
                }
              ]
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        Assert.Equal(2, rule.Actions.Count);

        // First action
        Assert.Equal("log", rule.Actions[0].Action);
        Assert.Null(rule.Actions[0].Condition);
        Assert.Equal("Alert!", rule.Actions[0].Parameters["message"]?.ToString());

        // Second action
        Assert.Equal("email", rule.Actions[1].Action);
        Assert.NotNull(rule.Actions[1].Condition);
        Assert.IsType<OperatorCondition>(rule.Actions[1].Condition);
    }

    [Fact]
    public void Parse_ActionWithNoSchedule_ScheduleIsNull()
    {
        const string json =
            """
            {
              "id": "no-schedule-test",
              "name": "No Schedule Test",
              "eventType": "Event1",
              "condition": { "operator": "eq", "operands": [{ "value": true }, { "value": true }] },
              "actions": [
                {
                  "action": "log",
                  "parameters": { "message": "Immediate by default" }
                }
              ]
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        Assert.Single(rule.Actions);
        Assert.Null(rule.Actions[0].Schedule);
    }

    [Fact]
    public void Parse_ActionWithImmediateSchedule_Success()
    {
        const string json =
            """
            {
              "id": "immediate-schedule-test",
              "name": "Immediate Schedule Test",
              "eventType": "Event1",
              "condition": { "operator": "eq", "operands": [{ "value": true }, { "value": true }] },
              "actions": [
                {
                  "action": "log",
                  "schedule": { "mode": "Immediate" },
                  "parameters": { "message": "Immediate explicitly" }
                }
              ]
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        var action = Assert.Single(rule.Actions);
        var schedule = Assert.IsType<ScheduleMetadata>(action.Schedule);
        Assert.Equal(ScheduleMode.Immediate, schedule.Mode);
        Assert.Null(schedule.Amount);
        Assert.Null(schedule.Unit);
        Assert.Null(schedule.ReferencePath);
    }

    [Fact]
    public void Parse_ActionWithDelaySchedule_Success()
    {
        const string json =
            """
            {
              "id": "delay-schedule-test",
              "name": "Delay Schedule Test",
              "eventType": "Event1",
              "condition": { "operator": "eq", "operands": [{ "value": true }, { "value": true }] },
              "actions": [
                {
                  "action": "log",
                  "schedule": {
                    "mode": "Delay",
                    "amount": 24,
                    "unit": "Hours"
                  },
                  "parameters": { "message": "Delayed" }
                }
              ]
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        var action = Assert.Single(rule.Actions);
        var schedule = Assert.IsType<ScheduleMetadata>(action.Schedule);
        Assert.Equal(ScheduleMode.Delay, schedule.Mode);
        Assert.Equal(24, schedule.Amount);
        Assert.Equal(ScheduleUnit.Hours, schedule.Unit);
        Assert.Null(schedule.ReferencePath);
    }

    [Fact]
    public void Parse_ActionWithAdvanceSchedule_Success()
    {
        const string json =
            """
            {
              "id": "advance-schedule-test",
              "name": "Advance Schedule Test",
              "eventType": "Event1",
              "condition": { "operator": "eq", "operands": [{ "value": true }, { "value": true }] },
              "actions": [
                {
                  "action": "log",
                  "schedule": {
                    "mode": "Advance",
                    "amount": 2,
                    "unit": "Hours",
                    "referencePath": "Order.DueDate"
                  },
                  "parameters": { "message": "Advance" }
                }
              ]
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        var action = Assert.Single(rule.Actions);
        var schedule = Assert.IsType<ScheduleMetadata>(action.Schedule);
        Assert.Equal(ScheduleMode.Advance, schedule.Mode);
        Assert.Equal(2, schedule.Amount);
        Assert.Equal(ScheduleUnit.Hours, schedule.Unit);
        Assert.Equal("Order.DueDate", schedule.ReferencePath);
    }

    [Fact]
    public void Parse_ScheduleEnumCaseInsensitive_Success()
    {
        const string json =
            """
            {
              "id": "case-insensitive-schedule-test",
              "name": "Case-insensitive Schedule Test",
              "eventType": "Event1",
              "condition": { "operator": "eq", "operands": [{ "value": true }, { "value": true }] },
              "actions": [
                {
                  "action": "log",
                  "schedule": {
                    "mode": "delay",
                    "amount": 30,
                    "unit": "minutes"
                  },
                  "parameters": { "message": "Case-insensitive enums" }
                }
              ]
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        var action = Assert.Single(rule.Actions);
        var schedule = Assert.IsType<ScheduleMetadata>(action.Schedule);
        Assert.Equal(ScheduleMode.Delay, schedule.Mode);
        Assert.Equal(30, schedule.Amount);
        Assert.Equal(ScheduleUnit.Minutes, schedule.Unit);
    }

    [Fact]
    public void Parse_CaseInsensitive_Success()
    {
        const string json =
            """
            {
              "ID": "test",
              "NAME": "Test",
              "EVENTTYPE": "Event1",
              "CONDITION": { "OPERATOR": "changed", "OPERANDS": [{ "PATH": "Field" }] },
              "ACTIONS": []
            }
            """;

        var rule = RuleJsonParser.Parse(json);

        Assert.Equal("test", rule.Id);
        Assert.Equal("Test", rule.Name);
        Assert.Equal("Event1", rule.EventType);
    }

    [Fact]
    public void Parse_VariousValueTypes_Success()
    {
        const string json =
            """
            [
              { "id": "r1", "name": "R1", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "value": null }] }, "actions": [] },
              { "id": "r2", "name": "R2", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "value": true }] }, "actions": [] },
              { "id": "r3", "name": "R3", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "value": "string" }] }, "actions": [] },
              { "id": "r4", "name": "R4", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "value": 42 }] }, "actions": [] },
              { "id": "r5", "name": "R5", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "value": 3.14 }] }, "actions": [] },
              { "id": "r6", "name": "R6", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "value": [1, 2, 3] }] }, "actions": [] }
            ]
            """;

        var rules = RuleJsonParser.ParseMany(json);

        Assert.Equal(6, rules.Count);
        Assert.Null(FirstValue(rules[0]).Value);
        Assert.True((bool)FirstValue(rules[1]).Value!);
        Assert.Equal("string", (string)FirstValue(rules[2]).Value!);
        Assert.Equal(42, (int)FirstValue(rules[3]).Value!);
        Assert.Equal(3.14, (double)FirstValue(rules[4]).Value!);
        Assert.IsType<List<object?>>(FirstValue(rules[5]).Value);
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsException()
    {
        const string invalidJson = "{ invalid json }";

        Assert.Throws<JsonException>(() => RuleJsonParser.Parse(invalidJson));
    }

    [Fact]
    public void Parse_MissingName_ThrowsRuleParseException()
    {
        const string json =
            """
            {
              "id": "test",
              "eventType": "Event1",
              "condition": { "operator": "changed", "operands": [{ "path": "Field" }] },
              "actions": []
            }
            """;

        var ex = Assert.Throws<RuleParseException>(() => RuleJsonParser.Parse(json));
        Assert.Equal("$.name", ex.Path);
    }

    [Fact]
    public void Parse_UnknownConditionType_ThrowsException()
    {
        const string json =
            """
            {
              "id": "test",
              "name": "Test",
              "eventType": "Event1",
              "condition": { "unknown": "property" },
              "actions": []
            }
            """;

        var ex = Assert.Throws<RuleParseException>(() => RuleJsonParser.Parse(json));
        Assert.Equal("$.condition", ex.Path);
    }

    [Fact]
    public void ParseMany_MultipleRules_Success()
    {
        const string json =
            """
            [
              { "id": "rule1", "name": "Rule1", "eventType": "E1", "condition": { "operator": "changed", "operands": [{ "path": "A" }] }, "actions": [] },
              { "id": "rule2", "name": "Rule2", "eventType": "E2", "condition": { "operator": "changed", "operands": [{ "path": "B" }] }, "actions": [] },
              { "id": "rule3", "name": "Rule3", "eventType": "E3", "condition": { "operator": "changed", "operands": [{ "path": "C" }] }, "actions": [] }
            ]
            """;

        var rules = RuleJsonParser.ParseMany(json);

        Assert.Equal(3, rules.Count);
        Assert.Equal("Rule1", rules[0].Name);
        Assert.Equal("Rule2", rules[1].Name);
        Assert.Equal("Rule3", rules[2].Name);
    }

    [Fact]
    public void Parse_Id_IsRead()
    {
        const string json = """{ "id": "custom-id-123", "name": "Test", "eventType": "Event1", "condition": { "operator": "changed", "operands": [{ "path": "Field" }] } }""";

        Assert.Equal("custom-id-123", RuleJsonParser.Parse(json).Id);
    }

    [Fact]
    public void Parse_ComplexRealWorldExample_Success()
    {
        const string json =
            """
            {
              "id": "product-price-changed",
              "name": "Product price changed",
              "eventType": "ProductUpdated",
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
                  "action": "log",
                  "parameters": {
                    "message": "Product price changed!",
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
        Assert.Equal("Product price changed", rule.Name);
        Assert.Equal("ProductUpdated", rule.EventType);
        Assert.Equal(2, rule.Priority);
        Assert.IsType<OperatorCondition>(rule.Condition);
        Assert.Single(rule.Actions);
        Assert.NotNull(rule.Actions[0].Condition);
    }

    [Fact]
    public void Parse_ActionParameters_AreParsedAsClrValues()
    {
        const string json =
            """
            {
              "id": "parameters",
              "name": "Parameters",
              "eventType": "E",
              "condition": { "operator": "eq", "operands": [{ "value": 1 }, { "value": 1 }] },
              "actions": [
                {
                  "action": "notify",
                  "parameters": {
                    "text": "hi", "count": 3, "big": 3000000000, "ratio": 0.25, "urgent": true, "none": null,
                    "tags": ["a", 1], "options": { "retry": 2 }
                  }
                }
              ]
            }
            """;

        var parameters = RuleJsonParser.Parse(json).Actions[0].Parameters;

        Assert.Equal("hi", parameters["text"]);
        Assert.Equal(3, parameters["count"]);
        Assert.Equal(3_000_000_000L, parameters["big"]);
        Assert.Equal(0.25, parameters["ratio"]);
        Assert.Equal(true, parameters["urgent"]);
        Assert.Null(parameters["none"]);
        Assert.Equal(new List<object?> { "a", 1 }, parameters["tags"]);
        Assert.Equal(new Dictionary<string, object?> { ["retry"] = 2 }, parameters["options"]);
        Assert.DoesNotContain(parameters.Values, v => v is JsonElement);
    }

    private static ValueOperand FirstValue(RuleDefinition rule) =>
        (ValueOperand)((OperatorCondition)rule.Condition).Operands[0];
}

using System.Text.Json;
using System.Text.Json.Nodes;
using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Actions;
using RuleForge.Engine;
using RuleForge.Evaluators;
using RuleForge.Operators;

namespace RuleForge.Tests.Integration;

public class RuleEngineIntegrationTests
{
    private readonly RuleEngine _engine;
    private readonly List<string> _logMessages;

    public RuleEngineIntegrationTests()
    {
        _logMessages = [];

        _engine = new RuleEngine(
            new ConditionEvaluator(BuiltInOperators.Create()),
            new DirectActionFactory([new TestLogAction(_logMessages)]));
    }

    [Fact]
    public async Task RuleEngine_SimpleEqualsCondition_ExecutesAction()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "price-equals-test",
            Name = "Price Equals Test",
            EventType = "ProductUpdated",
            Priority = 1,
            Condition = new OperatorCondition
            {
                Operator = "eq",
                Operands =
                [
                    new PathOperand { Path = "Price" },
                    new ValueOperand { Value = 100000 }
                ]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Price matches!" }
                }
            ]
        };

        var product = new { Price = 100000 };

        // Act
        var result = (await _engine.ExecuteAsync("ProductUpdated", product, [rule], TestContext.Current.CancellationToken)).Rules;

        // Assert
        Assert.Single(_logMessages);
        Assert.Equal("Price matches!", _logMessages[0]);
        Assert.Single(result);
        Assert.Equal("Price Equals Test", result[0].RuleName);
        Assert.Single(result[0].ActionResults);
        Assert.Equal(ActionExecutionStatus.ExecutedImmediately, result[0].ActionResults[0].Status);
    }

    [Fact]
    public async Task RuleEngine_ChangedCondition_DetectsChange()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "price-changed-test",
            Name = "Price Changed Test",
            EventType = "ProductUpdated",
            Priority = 1,
            Condition = new OperatorCondition
            {
                Operator = "changed",
                Operands = [new PathOperand { Path = "Price" }]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Price changed!" }
                }
            ]
        };

        var previous = new { Price = 100000 };
        var current = new { Price = 150000 };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", current, previous, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_logMessages);
        Assert.Equal("Price changed!", _logMessages[0]);
    }

    [Fact]
    public async Task RuleEngine_ChangedCondition_NoChange_DoesNotExecute()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "price-changed-test",
            Name = "Price Changed Test",
            EventType = "ProductUpdated",
            Priority = 1,
            Condition = new OperatorCondition
            {
                Operator = "changed",
                Operands = [new PathOperand { Path = "Price" }]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Price changed!" }
                }
            ]
        };

        var previous = new { Price = 100000 };
        var current = new { Price = 100000 };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", current, previous, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_logMessages);
    }

    [Fact]
    public async Task RuleEngine_AndCondition_BothTrue_Executes()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "price-and-status-test",
            Name = "Price And Status Test",
            EventType = "ProductUpdated",
            Priority = 1,
            Condition = new OperatorCondition
            {
                Operator = "and",
                Operands =
                [
                    new OperatorCondition
                    {
                        Operator = "eq",
                        Operands =
                        [
                            new PathOperand { Path = "Price" },
                            new ValueOperand { Value = 100000 }
                        ]
                    },
                    new OperatorCondition
                    {
                        Operator = "eq",
                        Operands =
                        [
                            new PathOperand { Path = "Status" },
                            new ValueOperand { Value = "Active" }
                        ]
                    }
                ]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Both conditions met!" }
                }
            ]
        };

        var product = new { Price = 100000, Status = "Active" };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", product, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_logMessages);
        Assert.Equal("Both conditions met!", _logMessages[0]);
    }

    [Fact]
    public async Task RuleEngine_OrCondition_OnlyOneTrue_Executes()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "price-or-status-test",
            Name = "Price Or Status Test",
            EventType = "ProductUpdated",
            Priority = 1,
            Condition = new OperatorCondition
            {
                Operator = "or",
                Operands =
                [
                    new OperatorCondition
                    {
                        Operator = "eq",
                        Operands =
                        [
                            new PathOperand { Path = "Price" },
                            new ValueOperand { Value = 200000 }
                        ]
                    },
                    new OperatorCondition
                    {
                        Operator = "eq",
                        Operands =
                        [
                            new PathOperand { Path = "Status" },
                            new ValueOperand { Value = "Active" }
                        ]
                    }
                ]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "At least one condition met!" }
                }
            ]
        };

        var product = new { Price = 100000, Status = "Active" };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", product, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_logMessages);
        Assert.Equal("At least one condition met!", _logMessages[0]);
    }

    [Fact]
    public async Task RuleEngine_NotCondition_Inverts()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "not-test",
            Name = "Not Test",
            EventType = "ProductUpdated",
            Priority = 1,
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
                            new ValueOperand { Value = 200000 }
                        ]
                    }
                ]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Price is not 200000!" }
                }
            ]
        };

        var product = new { Price = 100000 };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", product, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_logMessages);
        Assert.Equal("Price is not 200000!", _logMessages[0]);
    }

    [Fact]
    public async Task RuleEngine_PriorityOrdering_ExecutesHighPriorityFirst()
    {
        // Arrange
        var rules = new[]
        {
            new RuleDefinition
            {
                Id = "low-priority",
                Name = "Low Priority",
                EventType = "Test",
                Priority = 1,
                Condition = new OperatorCondition
                {
                    Operator = "eq",
                    Operands = [new ValueOperand { Value = true }, new ValueOperand { Value = true }]
                },
                Actions =
                [
                    new ActionDefinition
                    {
                        Action = "test-log",
                        Parameters = { ["message"] = "Low" }
                    }
                ]
            },
            new RuleDefinition
            {
                Id = "high-priority",
                Name = "High Priority",
                EventType = "Test",
                Priority = 10,
                Condition = new OperatorCondition
                {
                    Operator = "eq",
                    Operands = [new ValueOperand { Value = true }, new ValueOperand { Value = true }]
                },
                Actions =
                [
                    new ActionDefinition
                    {
                        Action = "test-log",
                        Parameters = { ["message"] = "High" }
                    }
                ]
            },
            new RuleDefinition
            {
                Id = "medium-priority",
                Name = "Medium Priority",
                EventType = "Test",
                Priority = 5,
                Condition = new OperatorCondition
                {
                    Operator = "eq",
                    Operands = [new ValueOperand { Value = true }, new ValueOperand { Value = true }]
                },
                Actions =
                [
                    new ActionDefinition
                    {
                        Action = "test-log",
                        Parameters = { ["message"] = "Medium" }
                    }
                ]
            }
        };

        // Act
        await _engine.ExecuteAsync("Test", new { }, rules, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, _logMessages.Count);
        Assert.Equal("High", _logMessages[0]);
        Assert.Equal("Medium", _logMessages[1]);
        Assert.Equal("Low", _logMessages[2]);
    }

    [Fact]
    public async Task RuleEngine_ActionWithCondition_OnlyExecutesWhenActionConditionMet()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "conditional-action-test",
            Name = "Conditional Action Test",
            EventType = "ProductUpdated",
            Priority = 1,
            Condition = new OperatorCondition
            {
                Operator = "eq",
                Operands = [new ValueOperand { Value = true }, new ValueOperand { Value = true }]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Action 1 - Always" }
                },
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Action 2 - Conditional" },
                    Condition = new OperatorCondition
                    {
                        Operator = "eq",
                        Operands =
                        [
                            new PathOperand { Path = "Status" },
                            new ValueOperand { Value = "Active" }
                        ]
                    }
                },
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Action 3 - Conditional" },
                    Condition = new OperatorCondition
                    {
                        Operator = "eq",
                        Operands =
                        [
                            new PathOperand { Path = "Status" },
                            new ValueOperand { Value = "Inactive" }
                        ]
                    }
                }
            ]
        };

        var product = new { Status = "Active" };

        // Act
        var result = (await _engine.ExecuteAsync("ProductUpdated", product, [rule], TestContext.Current.CancellationToken)).Rules;

        // Assert
        Assert.Equal(2, _logMessages.Count);
        Assert.Equal("Action 1 - Always", _logMessages[0]);
        Assert.Equal("Action 2 - Conditional", _logMessages[1]);
        Assert.Single(result);
        Assert.Equal(3, result[0].ActionResults.Count);
        Assert.Equal(ActionExecutionStatus.Skipped, result[0].ActionResults[2].Status);
    }

    [Fact]
    public async Task RuleEngine_NestedProperties_ResolvesCorrectly()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "nested-property-test",
            Name = "Nested Property Test",
            EventType = "UserUpdated",
            Priority = 1,
            Condition = new OperatorCondition
            {
                Operator = "eq",
                Operands =
                [
                    new PathOperand { Path = "Address.City" },
                    new ValueOperand { Value = "New York" }
                ]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "User in New York!" }
                }
            ]
        };

        var user = new TestUser
        {
            Name = "John",
            Address = new TestAddress
            {
                City = "New York",
                ZipCode = "10001"
            }
        };

        // Act
        await _engine.ExecuteAsync("UserUpdated", user, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_logMessages);
        Assert.Equal("User in New York!", _logMessages[0]);
    }

    [Fact]
    public async Task RuleEngine_MissingProperty_DoesNotCrash()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Id = "missing-property-test",
            Name = "Missing Property Test",
            EventType = "Test",
            Priority = 1,
            Condition = new OperatorCondition
            {
                Operator = "eq",
                Operands =
                [
                    new PathOperand { Path = "NonExistent" },
                    new ValueOperand { Value = "SomeValue" }
                ]
            },
            Actions =
            [
                new ActionDefinition
                {
                    Action = "test-log",
                    Parameters = { ["message"] = "Should not execute" }
                }
            ]
        };

        var data = new { ExistingProperty = "value" };

        // Act & Assert - Should not throw
        await _engine.ExecuteAsync("Test", data, [rule], TestContext.Current.CancellationToken);

        Assert.Empty(_logMessages);
    }

    [Fact]
    public async Task RuleEngine_JsonParsedRule_ExecutesCorrectly()
    {
        // Arrange
        var json = """
        {
          "id": "json-rule-test",
          "name": "JSON Rule Test",
          "eventType": "ProductUpdated",
          "priority": 5,
          "condition": {
            "operator": "and",
            "operands": [
              {
                "operator": "changed",
                "operands": [{ "path": "Price" }]
              },
              {
                "operator": "eq",
                "operands": [
                  { "path": "Status" },
                  { "value": "Active" }
                ]
              }
            ]
          },
          "actions": [
            {
              "action": "test-log",
              "parameters": { "message": "JSON rule executed!" }
            }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);
        var previous = new { Price = 100000, Status = "Active" };
        var current = new { Price = 150000, Status = "Active" };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", current, previous, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_logMessages);
        Assert.Equal("JSON rule executed!", _logMessages[0]);
    }

    [Fact]
    public async Task RuleEngine_JsonActionParameters_ReachActionAsTypedValues()
    {
        // Arrange
        var json = """
        {
          "id": "invoice-reminder",
          "name": "Invoice reminder",
          "eventType": "InvoiceDue",
          "condition": { "operator": "eq", "operands": [{ "path": "Paid" }, { "value": false }] },
          "actions": [
            {
              "action": "reminder",
              "parameters": { "amount": 250, "sendAt": "2026-05-01T09:00:00Z", "recipients": ["ada@example.com"] }
            }
          ]
        }
        """;

        var reminder = new ReminderAction();
        var engine = new RuleEngine(new ConditionEvaluator([new EqualsOperator()]), new DirectActionFactory([reminder]));

        // Act
        var result = await engine.ExecuteAsync(
            "InvoiceDue", new { Paid = false }, [RuleJsonParser.Parse(json)], TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.HasFailures);
        Assert.Equal(250m, reminder.Amount);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), reminder.SendAt);
        Assert.Equal(["ada@example.com"], reminder.Recipients);
    }

    [Fact]
    public async Task RuleEngine_JsonValuesAgainstTypedProperties_MatchByValue()
    {
        // Arrange
        var json = """
        {
          "id": "typed-values",
          "name": "Typed values",
          "eventType": "ProductUpdated",
          "condition": {
            "operator": "and",
            "operands": [
              { "operator": "eq", "operands": [{ "path": "Price" }, { "value": 100 }] },
              { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "active" }] },
              { "operator": "eq", "operands": [{ "path": "PublishedAt" }, { "value": "2026-05-01T14:00:00+02:00" }] }
            ]
          },
          "actions": [
            { "action": "test-log", "parameters": { "message": "Typed values matched!" } }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);
        var product = new TestProduct
        {
            Price = 100.00m,
            Status = TestProductStatus.Active,
            PublishedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero)
        };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", product, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Typed values matched!"], _logMessages);
    }

    [Fact]
    public async Task RuleEngine_ImmediateAndScheduledActions_ReportsBothOutcomes()
    {
        // Arrange
        var json = """
        {
          "id": "order-placed",
          "name": "Order placed",
          "eventType": "OrderPlaced",
          "condition": { "operator": "gt", "operands": [{ "path": "Total" }, { "value": 0 }] },
          "actions": [
            { "action": "test-log", "parameters": { "message": "Thanks for your order!" } },
            {
              "action": "test-log",
              "parameters": { "message": "Reminder" },
              "schedule": { "mode": "Advance", "amount": 2, "unit": "Hours", "referencePath": "DueAt" }
            }
          ]
        }
        """;

        var dueAt = new DateTimeOffset(2026, 10, 9, 17, 0, 0, TimeSpan.Zero);
        var order = new { Total = 120m, DueAt = dueAt };

        // Act
        var result = await _engine.ExecuteAsync("OrderPlaced", order, [RuleJsonParser.Parse(json)], TestContext.Current.CancellationToken);

        // Assert
        var rule = Assert.Single(result.Rules);
        Assert.Equal(RuleExecutionStatus.Matched, rule.Status);
        Assert.Equal(
            [ActionExecutionStatus.ExecutedImmediately, ActionExecutionStatus.Scheduled],
            rule.ActionResults.Select(a => a.Status));
        Assert.Equal(dueAt.AddHours(-2), rule.ActionResults[1].ScheduledFor);

        var scheduled = Assert.Single(result.ScheduledActions);
        Assert.Equal(1, scheduled.Result.ActionIndex);
        Assert.Equal("Reminder", scheduled.Action.GetParameter<string>("message"));

        Assert.Equal(["Thanks for your order!"], _logMessages);
    }

    [Fact]
    public async Task RuleEngine_FailingAction_IsReportedAndLaterActionsStillRun()
    {
        // Arrange
        var json = """
        {
          "id": "sync-customer",
          "name": "Sync customer",
          "eventType": "CustomerUpdated",
          "condition": { "operator": "changed", "operands": [{ "path": "Email" }] },
          "actions": [
            { "action": "failing" },
            { "action": "test-log", "parameters": { "message": "Audit written" } }
          ]
        }
        """;

        var engine = new RuleEngine(
            new ConditionEvaluator(BuiltInOperators.Create()),
            new DirectActionFactory([new FailingAction(), new TestLogAction(_logMessages)]));

        // Act
        var result = await engine.ExecuteAsync(
            "CustomerUpdated", new { Email = "new@example.com" }, new { Email = "old@example.com" },
            [RuleJsonParser.Parse(json)], TestContext.Current.CancellationToken);

        // Assert
        var actions = result.Rules[0].ActionResults;
        Assert.Equal(ActionExecutionStatus.Failed, actions[0].Status);
        Assert.IsType<TimeoutException>(actions[0].Error);
        Assert.Equal(ActionExecutionStatus.ExecutedImmediately, actions[1].Status);
        Assert.True(result.HasFailures);
        Assert.Equal(["Audit written"], _logMessages);
    }

    [Fact]
    public async Task RuleEngine_ScopeVariables_CompareCurrentWithPrevious()
    {
        // Arrange
        var json = """
        {
          "id": "price-dropped",
          "name": "Price dropped",
          "eventType": "ProductUpdated",
          "condition": {
            "operator": "and",
            "operands": [
              { "operator": "changed", "operands": [{ "path": "$current.Price" }] },
              { "operator": "lt", "operands": [{ "path": "$current.Price" }, { "path": "$previous.Price" }] }
            ]
          },
          "actions": [
            { "action": "test-log", "parameters": { "message": "Price dropped!" } }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        // Act
        await _engine.ExecuteAsync("ProductUpdated", new { Price = 90 }, new { Price = 100 }, [rule], TestContext.Current.CancellationToken);
        await _engine.ExecuteAsync("ProductUpdated", new { Price = 110 }, new { Price = 100 }, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Price dropped!"], _logMessages);
    }

    [Fact]
    public async Task RuleEngine_OrderingOperators_MatchThresholdsOnTypedProperties()
    {
        // Arrange
        var json = """
        {
          "id": "affordable-and-recent",
          "name": "Affordable and recent",
          "eventType": "ProductUpdated",
          "condition": {
            "operator": "and",
            "operands": [
              { "operator": "gte", "operands": [{ "path": "Price" }, { "value": 100 }] },
              { "operator": "lt", "operands": [{ "path": "Price" }, { "value": 250.5 }] },
              { "operator": "gt", "operands": [{ "path": "PublishedAt" }, { "value": "2026-04-30" }] }
            ]
          },
          "actions": [
            { "action": "test-log", "parameters": { "message": "Within range!" } }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);
        var inRange = new TestProduct { Price = 100m, PublishedAt = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero) };
        var tooExpensive = new TestProduct { Price = 250.5m, PublishedAt = inRange.PublishedAt };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", inRange, [rule], TestContext.Current.CancellationToken);
        await _engine.ExecuteAsync("ProductUpdated", tooExpensive, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Within range!"], _logMessages);
    }

    [Fact]
    public async Task RuleEngine_NamesInAnyCase_MatchRegisteredNames()
    {
        // Arrange
        var json = """
        {
          "id": "shouting-rule",
          "name": "Shouting rule",
          "eventType": "PRODUCTUPDATED",
          "condition": {
            "operator": "AND",
            "operands": [
              { "operator": "Gte", "operands": [{ "path": "price" }, { "value": 100 }] },
              { "operator": "EQ", "operands": [{ "path": "STATUS" }, { "value": "Active" }] }
            ]
          },
          "actions": [
            { "action": "Test-Log", "parameters": { "message": "Case ignored!" } }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);
        var product = new TestProduct { Price = 150m, Status = TestProductStatus.Active };

        // Act
        await _engine.ExecuteAsync("ProductUpdated", product, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Case ignored!"], _logMessages);
    }

    public static TheoryData<string> JsonStateKinds() => ["JsonElement", "JsonNode"];

    [Theory]
    [MemberData(nameof(JsonStateKinds))]
    public async Task RuleEngine_JsonState_EvaluatesConditionsWithPreviousState(string kind)
    {
        // Arrange
        var json = """
        {
          "id": "vip-order-shipped",
          "name": "VIP order shipped",
          "eventType": "OrderUpdated",
          "condition": {
            "operator": "and",
            "operands": [
              { "operator": "changed", "operands": [{ "path": "Status" }] },
              { "operator": "eq", "operands": [{ "path": "status" }, { "value": "Shipped" }] },
              { "operator": "eq", "operands": [{ "path": "$previous.Status" }, { "value": "Placed" }] },
              { "operator": "gte", "operands": [{ "path": "Total" }, { "value": 89.9 }] },
              { "operator": "lt", "operands": [{ "path": "Lines" }, { "value": 10 }] },
              { "path": "Customer.Verified" }
            ]
          },
          "actions": [
            { "action": "test-log", "parameters": { "message": "Shipped" } },
            {
              "action": "test-log",
              "parameters": { "message": "VIP" },
              "condition": { "operator": "eq", "operands": [{ "path": "Customer.Tier" }, { "value": "VIP" }] }
            },
            {
              "action": "test-log",
              "parameters": { "message": "Basic" },
              "condition": { "operator": "eq", "operands": [{ "path": "Customer.Tier" }, { "value": "Basic" }] }
            }
          ]
        }
        """;

        var current = ParseState(kind, """
            { "Status": "Shipped", "Total": 89.90, "Lines": 2, "Customer": { "Tier": "VIP", "Verified": true } }
            """);
        var previous = ParseState(kind, """
            { "Status": "Placed", "Total": 89.90, "Lines": 2, "Customer": { "Tier": "VIP", "Verified": true } }
            """);

        // Act
        var result = await _engine.ExecuteAsync(
            "OrderUpdated", current, previous, [RuleJsonParser.Parse(json)], TestContext.Current.CancellationToken);

        // Assert
        var rule = Assert.Single(result.Rules);
        Assert.Equal(RuleExecutionStatus.Matched, rule.Status);
        Assert.Equal(
            [ActionExecutionStatus.ExecutedImmediately, ActionExecutionStatus.ExecutedImmediately, ActionExecutionStatus.Skipped],
            rule.ActionResults.Select(a => a.Status));
        Assert.Equal(["Shipped", "VIP"], _logMessages);
    }

    [Theory]
    [MemberData(nameof(JsonStateKinds))]
    public async Task RuleEngine_JsonStateWithIsoDate_SchedulesFromReferencePath(string kind)
    {
        // Arrange
        var json = """
        {
          "id": "invoice-issued",
          "name": "Invoice issued",
          "eventType": "InvoiceIssued",
          "condition": { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Open" }] },
          "actions": [
            {
              "action": "test-log",
              "parameters": { "message": "Payment due soon" },
              "schedule": { "mode": "Advance", "amount": 2, "unit": "Days", "referencePath": "DueAt" }
            }
          ]
        }
        """;

        var invoice = ParseState(kind, """{ "Status": "Open", "DueAt": "2026-10-24T11:00:00+02:00" }""");

        // Act
        var result = await _engine.ExecuteAsync("InvoiceIssued", invoice, [RuleJsonParser.Parse(json)], TestContext.Current.CancellationToken);

        // Assert
        var scheduled = Assert.Single(result.ScheduledActions);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero), scheduled.Result.ScheduledFor);
        Assert.Empty(_logMessages);
    }

    [Theory]
    [MemberData(nameof(JsonStateKinds))]
    public async Task RuleEngine_JsonStateDatesAndObjects_CompareByMeaning(string kind)
    {
        // Arrange: a date range in UTC against a date with a +02:00 offset, and an object that didn't change
        var json = """
        {
          "id": "delivered-in-may-address-unchanged",
          "name": "Delivered in May, address unchanged",
          "eventType": "OrderUpdated",
          "condition": {
            "operator": "and",
            "operands": [
              { "operator": "gte", "operands": [{ "path": "DeliveredAt" }, { "value": "2026-05-01T00:00:00Z" }] },
              { "operator": "lt", "operands": [{ "path": "DeliveredAt" }, { "value": "2026-06-01" }] },
              { "operator": "not", "operands": [{ "operator": "changed", "operands": [{ "path": "Address" }] }] },
              { "operator": "not", "operands": [{ "operator": "changed", "operands": [{ "path": "Total" }] }] }
            ]
          },
          "actions": [{ "action": "test-log", "parameters": { "message": "Matched" } }]
        }
        """;

        var current = ParseState(kind, """
            { "DeliveredAt": "2026-06-01T01:30:00+02:00", "Total": 100, "Address": { "City": "Paris", "Zip": "75001" } }
            """);
        var previous = ParseState(kind, """
            { "DeliveredAt": null, "Total": 100.0, "Address": { "Zip": "75001", "City": "Paris" } }
            """);

        // Act
        await _engine.ExecuteAsync("OrderUpdated", current, previous, [RuleJsonParser.Parse(json)], TestContext.Current.CancellationToken);

        // Assert: 01:30+02:00 is 23:30Z on 31 May, so it's inside the range
        Assert.Equal(["Matched"], _logMessages);
    }

    private static object ParseState(string kind, string json) => kind switch
    {
        "JsonElement" => JsonSerializer.Deserialize<object>(json)!,
        _ => JsonNode.Parse(json)!
    };

    // Helper class for testing
    private class TestLogAction(List<string> messages) : IAction
    {

        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (action.Parameters.TryGetValue("message", out var message))
            {
                messages.Add(message?.ToString() ?? string.Empty);
            }

            return Task.FromResult(ActionResult.Success());
        }
    }

    private sealed class FailingAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("The CRM didn't respond.");
    }

    private sealed class ReminderAction : IAction
    {
        public decimal Amount { get; private set; }
        public DateTimeOffset SendAt { get; private set; }
        public IReadOnlyList<object?> Recipients { get; private set; } = [];

        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
        {
            Amount = action.GetParameter<decimal>("amount");
            SendAt = action.GetParameter<DateTimeOffset>("sendAt");
            Recipients = action.GetParameter<IReadOnlyList<object?>>("recipients");

            return Task.FromResult(ActionResult.Success());
        }
    }

    private class TestUser
    {
        public string Name { get; set; } = string.Empty;
        public TestAddress Address { get; set; } = new();
    }

    private class TestAddress
    {
        public string City { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
    }

    private class TestProduct
    {
        public decimal Price { get; set; }
        public TestProductStatus Status { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
    }

    private enum TestProductStatus
    {
        Draft,
        Active
    }
}



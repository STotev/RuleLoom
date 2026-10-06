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

        var operators = new List<IConditionOperator>
        {
            new EqualsOperator(),
            new ChangedOperator(),
            new AndOperator(),
            new OrOperator(),
            new NotOperator(),
            new GreaterThanOperator(),
            new GreaterThanOrEqualOperator(),
            new LessThanOperator(),
            new LessThanOrEqualOperator()
        };

        var actions = new List<IActionExecutor>
        {
            new TestLogAction(_logMessages)
        };

        _engine = new RuleEngine(new ConditionEvaluator(operators), new DirectActionExecutorFactory(actions));
    }

    [Fact]
    public async Task RuleEngine_SimpleEqualsCondition_ExecutesAction()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Name = "Price Equals Test",
            EventType = "PropertyUpdated",
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
                    Type = "test-log",
                    Parameters = { ["message"] = "Price matches!" }
                }
            ]
        };

        var property = new { Price = 100000 };

        // Act
        var result = (await _engine.ExecuteAsync("PropertyUpdated", property, [rule], TestContext.Current.CancellationToken)).Rules;

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
            Name = "Price Changed Test",
            EventType = "PropertyUpdated",
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
                    Type = "test-log",
                    Parameters = { ["message"] = "Price changed!" }
                }
            ]
        };

        var previous = new { Price = 100000 };
        var current = new { Price = 150000 };

        // Act
        await _engine.ExecuteAsync("PropertyUpdated", current, previous, [rule], TestContext.Current.CancellationToken);

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
            Name = "Price Changed Test",
            EventType = "PropertyUpdated",
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
                    Type = "test-log",
                    Parameters = { ["message"] = "Price changed!" }
                }
            ]
        };

        var previous = new { Price = 100000 };
        var current = new { Price = 100000 };

        // Act
        await _engine.ExecuteAsync("PropertyUpdated", current, previous, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_logMessages);
    }

    [Fact]
    public async Task RuleEngine_AndCondition_BothTrue_Executes()
    {
        // Arrange
        var rule = new RuleDefinition
        {
            Name = "Price And Status Test",
            EventType = "PropertyUpdated",
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
                    Type = "test-log",
                    Parameters = { ["message"] = "Both conditions met!" }
                }
            ]
        };

        var property = new { Price = 100000, Status = "Active" };

        // Act
        await _engine.ExecuteAsync("PropertyUpdated", property, [rule], TestContext.Current.CancellationToken);

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
            Name = "Price Or Status Test",
            EventType = "PropertyUpdated",
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
                    Type = "test-log",
                    Parameters = { ["message"] = "At least one condition met!" }
                }
            ]
        };

        var property = new { Price = 100000, Status = "Active" };

        // Act
        await _engine.ExecuteAsync("PropertyUpdated", property, [rule], TestContext.Current.CancellationToken);

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
            Name = "Not Test",
            EventType = "PropertyUpdated",
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
                    Type = "test-log",
                    Parameters = { ["message"] = "Price is not 200000!" }
                }
            ]
        };

        var property = new { Price = 100000 };

        // Act
        await _engine.ExecuteAsync("PropertyUpdated", property, [rule], TestContext.Current.CancellationToken);

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
                        Type = "test-log",
                        Parameters = { ["message"] = "Low" }
                    }
                ]
            },
            new RuleDefinition
            {
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
                        Type = "test-log",
                        Parameters = { ["message"] = "High" }
                    }
                ]
            },
            new RuleDefinition
            {
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
                        Type = "test-log",
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
            Name = "Conditional Action Test",
            EventType = "PropertyUpdated",
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
                    Type = "test-log",
                    Parameters = { ["message"] = "Action 1 - Always" }
                },
                new ActionDefinition
                {
                    Type = "test-log",
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
                    Type = "test-log",
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

        var property = new { Status = "Active" };

        // Act
        var result = (await _engine.ExecuteAsync("PropertyUpdated", property, [rule], TestContext.Current.CancellationToken)).Rules;

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
                    Type = "test-log",
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
                    Type = "test-log",
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
          "name": "JSON Rule Test",
          "eventType": "PropertyUpdated",
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
              "type": "test-log",
              "parameters": { "message": "JSON rule executed!" }
            }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);
        var previous = new { Price = 100000, Status = "Active" };
        var current = new { Price = 150000, Status = "Active" };

        // Act
        await _engine.ExecuteAsync("PropertyUpdated", current, previous, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_logMessages);
        Assert.Equal("JSON rule executed!", _logMessages[0]);
    }

    [Fact]
    public async Task RuleEngine_JsonValuesAgainstTypedProperties_MatchByValue()
    {
        // Arrange
        var json = """
        {
          "name": "Typed values",
          "eventType": "ListingUpdated",
          "condition": {
            "operator": "and",
            "operands": [
              { "operator": "eq", "operands": [{ "path": "Price" }, { "value": 100 }] },
              { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "active" }] },
              { "operator": "eq", "operands": [{ "path": "ListedAt" }, { "value": "2026-05-01T14:00:00+02:00" }] }
            ]
          },
          "actions": [
            { "type": "test-log", "parameters": { "message": "Typed values matched!" } }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);
        var listing = new TestListing
        {
            Price = 100.00m,
            Status = TestListingStatus.Active,
            ListedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero)
        };

        // Act
        await _engine.ExecuteAsync("ListingUpdated", listing, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Typed values matched!"], _logMessages);
    }

    [Fact]
    public async Task RuleEngine_ScopeVariables_CompareCurrentWithPrevious()
    {
        // Arrange
        var json = """
        {
          "name": "Price dropped",
          "eventType": "PropertyUpdated",
          "condition": {
            "operator": "and",
            "operands": [
              { "operator": "changed", "operands": [{ "path": "$current.Price" }] },
              { "operator": "lt", "operands": [{ "path": "$current.Price" }, { "path": "$previous.Price" }] }
            ]
          },
          "actions": [
            { "type": "test-log", "parameters": { "message": "Price dropped!" } }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);

        // Act
        await _engine.ExecuteAsync("PropertyUpdated", new { Price = 90 }, new { Price = 100 }, [rule], TestContext.Current.CancellationToken);
        await _engine.ExecuteAsync("PropertyUpdated", new { Price = 110 }, new { Price = 100 }, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Price dropped!"], _logMessages);
    }

    [Fact]
    public async Task RuleEngine_OrderingOperators_MatchThresholdsOnTypedProperties()
    {
        // Arrange
        var json = """
        {
          "name": "Affordable and recent",
          "eventType": "ListingUpdated",
          "condition": {
            "operator": "and",
            "operands": [
              { "operator": "gte", "operands": [{ "path": "Price" }, { "value": 100 }] },
              { "operator": "lt", "operands": [{ "path": "Price" }, { "value": 250.5 }] },
              { "operator": "gt", "operands": [{ "path": "ListedAt" }, { "value": "2026-04-30" }] }
            ]
          },
          "actions": [
            { "type": "test-log", "parameters": { "message": "Within range!" } }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);
        var inRange = new TestListing { Price = 100m, ListedAt = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero) };
        var tooExpensive = new TestListing { Price = 250.5m, ListedAt = inRange.ListedAt };

        // Act
        await _engine.ExecuteAsync("ListingUpdated", inRange, [rule], TestContext.Current.CancellationToken);
        await _engine.ExecuteAsync("ListingUpdated", tooExpensive, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Within range!"], _logMessages);
    }

    [Fact]
    public async Task RuleEngine_NamesInAnyCase_MatchRegisteredNames()
    {
        // Arrange
        var json = """
        {
          "name": "Shouting rule",
          "eventType": "LISTINGUPDATED",
          "condition": {
            "operator": "AND",
            "operands": [
              { "operator": "Gte", "operands": [{ "path": "price" }, { "value": 100 }] },
              { "operator": "EQ", "operands": [{ "path": "STATUS" }, { "value": "Active" }] }
            ]
          },
          "actions": [
            { "type": "Test-Log", "parameters": { "message": "Case ignored!" } }
          ]
        }
        """;

        var rule = RuleJsonParser.Parse(json);
        var listing = new TestListing { Price = 150m, Status = TestListingStatus.Active };

        // Act
        await _engine.ExecuteAsync("ListingUpdated", listing, [rule], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Case ignored!"], _logMessages);
    }

    // Helper class for testing
    private class TestLogAction(List<string> messages) : IActionExecutor
    {
        public string Type => "test-log";

        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (action.Parameters.TryGetValue("message", out var message))
            {
                messages.Add(message?.ToString() ?? string.Empty);
            }

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

    private class TestListing
    {
        public decimal Price { get; set; }
        public TestListingStatus Status { get; set; }
        public DateTimeOffset ListedAt { get; set; }
    }

    private enum TestListingStatus
    {
        Draft,
        Active
    }
}



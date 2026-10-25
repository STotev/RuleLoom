using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Engine;
using RuleLoom.Operators;

namespace RuleLoom.Tests;

public class RuleEngineTests
{
    [Fact]
    public async Task ExecuteAsync_ActionExecutesSuccessfully_ReturnsPerRuleResults()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var executor = new DelegatingAction((_, _, _) => Task.FromResult(ActionResult.Success("done")));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["ok-action"] = executor
        });
        var engine = Engine(evaluator, factory);
        RuleDefinition[] rules = [CreateRule("Rule A", "ok-action")];

        var result = (await engine.ExecuteAsync("Event", new { }, rules, TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Equal("Rule A", result[0].RuleName);
        Assert.Single(result[0].ActionResults);
        Assert.Equal(ActionExecutionStatus.ExecutedImmediately, result[0].ActionResults[0].Status);
        Assert.Equal("done", result[0].ActionResults[0].Message);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownActionType_ReturnsFailedResultAndContinues()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var executor = new DelegatingAction((_, _, _) => Task.FromResult(ActionResult.Success("known-success")));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["known"] = executor
        });
        var engine = Engine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Id = "rule-a",
            Name = "Rule A",
            EventType = "Event",
            Condition = RuleCondition,
            Actions =
            [
                new ActionDefinition { Action = "unknown" },
                new ActionDefinition { Action = "known" }
            ]
        };

        var result = (await engine.ExecuteAsync("Event", new { }, [rule], TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Equal(2, result[0].ActionResults.Count);
        Assert.Equal(ActionExecutionStatus.Failed, result[0].ActionResults[0].Status);
        Assert.NotNull(result[0].ActionResults[0].Error);
        Assert.Equal(ActionExecutionStatus.ExecutedImmediately, result[0].ActionResults[1].Status);
        Assert.Equal("known-success", result[0].ActionResults[1].Message);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutorThrows_ReturnsFailedResultAndContinues()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var fail = new DelegatingAction((_, _, _) => throw new InvalidOperationException("boom"));
        var ok = new DelegatingAction((_, _, _) => Task.FromResult(ActionResult.Success("ok")));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["fails"] = fail,
            ["ok"] = ok
        });
        var engine = Engine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Id = "rule-a",
            Name = "Rule A",
            EventType = "Event",
            Condition = RuleCondition,
            Actions =
            [
                new ActionDefinition { Action = "fails" },
                new ActionDefinition { Action = "ok" }
            ]
        };

        var result = (await engine.ExecuteAsync("Event", new { }, [rule], TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Equal(2, result[0].ActionResults.Count);
        Assert.Equal(ActionExecutionStatus.Failed, result[0].ActionResults[0].Status);
        Assert.IsType<InvalidOperationException>(result[0].ActionResults[0].Error);
        Assert.Equal(ActionExecutionStatus.ExecutedImmediately, result[0].ActionResults[1].Status);
    }

    [Fact]
    public async Task ExecuteAsync_ActionConditionFalse_ReturnsSkippedResult()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true, ActionConditionResult: false);
        var executor = new DelegatingAction((_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["ok"] = executor
        });
        var engine = Engine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Id = "rule-a",
            Name = "Rule A",
            EventType = "Event",
            Condition = RuleCondition,
            Actions =
            [
                new ActionDefinition
                {
                    Action = "ok",
                    Condition = ActionCondition
                }
            ]
        };

        var result = (await engine.ExecuteAsync("Event", new { }, [rule], TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Single(result[0].ActionResults);
        Assert.Equal(ActionExecutionStatus.Skipped, result[0].ActionResults[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_RuleConditionFalse_ReturnsNotMatchedWithoutActions()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: false);
        var factory = new TestActionFactory(new Dictionary<string, IAction>());
        var engine = Engine(evaluator, factory);
        RuleDefinition[] rules = [CreateRule("Rule A", "missing")];

        var result = (await engine.ExecuteAsync("Event", new { }, rules, TestContext.Current.CancellationToken)).Rules;

        var rule = Assert.Single(result);
        Assert.Equal(RuleExecutionStatus.NotMatched, rule.Status);
        Assert.Empty(rule.ActionResults);
        Assert.Null(rule.Error);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleRules_AggregatesPerRuleResultsInPriorityOrder()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var executor = new DelegatingAction((_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["ok"] = executor
        });
        var engine = Engine(evaluator, factory);
        var rules = new[]
        {
            CreateRule("Low", "ok", priority: 1),
            CreateRule("High", "ok", priority: 10),
            CreateRule("Medium", "ok", priority: 5)
        };

        var result = (await engine.ExecuteAsync("Event", new { }, rules, TestContext.Current.CancellationToken)).Rules;

        Assert.Equal(3, result.Count);
        Assert.Equal("High", result[0].RuleName);
        Assert.Equal("Medium", result[1].RuleName);
        Assert.Equal("Low", result[2].RuleName);
        Assert.All(result, item =>
        {
            Assert.Single(item.ActionResults);
            Assert.Equal(ActionExecutionStatus.ExecutedImmediately, item.ActionResults[0].Status);
        });
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var executor = new DelegatingAction((_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(ActionResult.Success());
            });
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["cancellable"] = executor
        });
        var engine = Engine(evaluator, factory);
        RuleDefinition[] rules = [CreateRule("Rule A", "cancellable")];
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.ExecuteAsync("Event", new { }, rules, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_DelaySchedule_ReturnsScheduledResultAndSkipsExecutor()
    {
        var fixedNow = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var executor = new CountingAction((_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["scheduled"] = executor
        });
        var engine = Engine(evaluator, factory, new FixedTimeProvider(fixedNow));
        var rule = new RuleDefinition
        {
            Id = "rule-a",
            Name = "Rule A",
            EventType = "Event",
            Condition = RuleCondition,
            Actions =
            [
                new ActionDefinition
                {
                    Action = "scheduled",
                    Schedule = new ScheduleMetadata
                    {
                        Mode = ScheduleMode.Delay,
                        Amount = 2,
                        Unit = ScheduleUnit.Hours
                    }
                }
            ]
        };

        var result = (await engine.ExecuteAsync("Event", new { }, [rule], TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Single(result[0].ActionResults);
        var actionResult = result[0].ActionResults[0];
        Assert.Equal(ActionExecutionStatus.Scheduled, actionResult.Status);
        Assert.Equal(fixedNow.AddHours(2), actionResult.ScheduledFor);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task ExecuteAsync_AdvanceScheduleWithReferencePath_ReturnsScheduledResult()
    {
        var fixedNow = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var dueDate = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var executor = new CountingAction((_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["scheduled"] = executor
        });
        var engine = Engine(evaluator, factory, new FixedTimeProvider(fixedNow));
        var current = new OrderWithDateTimeOffset { DueDate = dueDate };
        var rule = new RuleDefinition
        {
            Id = "rule-a",
            Name = "Rule A",
            EventType = "Event",
            Condition = RuleCondition,
            Actions =
            [
                new ActionDefinition
                {
                    Action = "scheduled",
                    Schedule = new ScheduleMetadata
                    {
                        Mode = ScheduleMode.Advance,
                        Amount = 2,
                        Unit = ScheduleUnit.Hours,
                        ReferencePath = "DueDate"
                    }
                }
            ]
        };

        var result = (await engine.ExecuteAsync("Event", current, [rule], TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Single(result[0].ActionResults);
        var actionResult = result[0].ActionResults[0];
        Assert.Equal(ActionExecutionStatus.Scheduled, actionResult.Status);
        Assert.Equal(dueDate.AddHours(-2), actionResult.ScheduledFor);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task ExecuteAsync_ImmediateModeSchedule_ExecutesImmediately()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var executor = new CountingAction((_, _, _) => Task.FromResult(ActionResult.Success("executed")));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["ok"] = executor
        });
        var engine = Engine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Id = "rule-a",
            Name = "Rule A",
            EventType = "Event",
            Condition = RuleCondition,
            Actions =
            [
                new ActionDefinition
                {
                    Action = "ok",
                    Schedule = new ScheduleMetadata
                    {
                        Mode = ScheduleMode.Immediate,
                        Amount = 3,
                        Unit = ScheduleUnit.Minutes
                    }
                }
            ]
        };

        var result = (await engine.ExecuteAsync("Event", new { }, [rule], TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Single(result[0].ActionResults);
        Assert.Equal(ActionExecutionStatus.ExecutedImmediately, result[0].ActionResults[0].Status);
        Assert.Equal(1, executor.ExecutionCount);
    }

    [Fact]
    public async Task ExecuteAsync_ActionConditionFalseWithSchedule_ReturnsSkipped()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true, ActionConditionResult: false);
        var executor = new CountingAction((_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["scheduled"] = executor
        });
        var engine = Engine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Id = "rule-a",
            Name = "Rule A",
            EventType = "Event",
            Condition = RuleCondition,
            Actions =
            [
                new ActionDefinition
                {
                    Action = "scheduled",
                    Condition = ActionCondition,
                    Schedule = new ScheduleMetadata
                    {
                        Mode = ScheduleMode.Delay,
                        Amount = 1,
                        Unit = ScheduleUnit.Hours
                    }
                }
            ]
        };

        var result = (await engine.ExecuteAsync("Event", new { }, [rule], TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Single(result[0].ActionResults);
        Assert.Equal(ActionExecutionStatus.Skipped, result[0].ActionResults[0].Status);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidReferencePath_ReturnsFailedResult()
    {
        var evaluator = new TestConditionEvaluator(ConditionResult: true);
        var executor = new CountingAction((_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionFactory(new Dictionary<string, IAction>
        {
            ["scheduled"] = executor
        });
        var engine = Engine(evaluator, factory);
        var current = new { DueDate = "not-a-date" };
        var rule = new RuleDefinition
        {
            Id = "rule-a",
            Name = "Rule A",
            EventType = "Event",
            Condition = RuleCondition,
            Actions =
            [
                new ActionDefinition
                {
                    Action = "scheduled",
                    Schedule = new ScheduleMetadata
                    {
                        Mode = ScheduleMode.Delay,
                        Amount = 1,
                        Unit = ScheduleUnit.Hours,
                        ReferencePath = "DueDate"
                    }
                }
            ]
        };

        var result = (await engine.ExecuteAsync("Event", current, [rule], TestContext.Current.CancellationToken)).Rules;

        Assert.Single(result);
        Assert.Single(result[0].ActionResults);
        var actionResult = result[0].ActionResults[0];
        Assert.Equal(ActionExecutionStatus.Failed, actionResult.Status);
        Assert.IsType<InvalidOperationException>(actionResult.Error);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Theory]
    [InlineData("#now", "reserved for functions")]
    [InlineData("$previous.@customer.Tier", "Contexts have no current or previous state")]
    public async Task ExecuteAsync_InvalidPathInARuleBuiltInCode_FailsTheRule(string path, string message)
    {
        var engine = Engine(new TestConditionEvaluator(ConditionResult: true), new TestActionFactory(new Dictionary<string, IAction>()));
        var rule = new RuleDefinition
        {
            Id = "r", Name = "R", EventType = "Event",
            Condition = Conditions.Op("eq", Conditions.Path(path), Conditions.Value(1))
        };

        var result = (await engine.ExecuteAsync("Event", new { }, [rule], TestContext.Current.CancellationToken)).Rules[0];

        Assert.Equal(RuleExecutionStatus.Failed, result.Status);
        Assert.Contains(message, result.Error!.Message);
    }

    private static RuleDefinition CreateRule(string name, string actionType, int priority = 1) =>
        new()
        {
            Id = name,
            Name = name,
            EventType = "Event",
            Priority = priority,
            Condition = RuleCondition,
            Actions = [new ActionDefinition { Action = actionType }]
        };

    // Rule conditions are "rule-condition" and action conditions "action-condition", with fixed results.
    private static readonly OperatorCondition RuleCondition = Conditions.Op("rule-condition");
    private static readonly OperatorCondition ActionCondition = Conditions.Op("action-condition");

    private static RuleEngine Engine(TestConditionEvaluator conditions, TestActionFactory actions, TimeProvider? timeProvider = null)
    {
        var catalog = new TestCatalog().Operators(
        [
            .. BuiltInOperators.Create(),
            new CountingOperator("rule-condition", conditions.ConditionResult),
            new CountingOperator("action-condition", conditions.ActionConditionResult)
        ]);

        foreach (var (name, action) in actions.Executors)
            catalog.Action(name, action);

        return catalog.Engine(timeProvider);
    }

    private sealed record TestActionFactory(IReadOnlyDictionary<string, IAction> Executors);

    private sealed class DelegatingAction(Func<ActionDefinition, EvaluationContext, CancellationToken, Task<ActionResult>> execute) : IAction
    {

        public Task<ActionResult> ExecuteAsync(
            ActionDefinition action,
            EvaluationContext context,
            CancellationToken cancellationToken = default) =>
            execute(action, context, cancellationToken);
    }

    private sealed class CountingAction(Func<ActionDefinition, EvaluationContext, CancellationToken, Task<ActionResult>> execute) : IAction
    {
        public int ExecutionCount { get; private set; }

        public Task<ActionResult> ExecuteAsync(
            ActionDefinition action,
            EvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return execute(action, context, cancellationToken);
        }
    }

    private sealed record TestConditionEvaluator(bool ConditionResult, bool ActionConditionResult = true);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class OrderWithDateTimeOffset
    {
        public DateTimeOffset DueDate { get; init; }
    }
}

using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Abstractions.Scheduling;
using RuleForge.Engine;

namespace RuleForge.Tests;

public class RuleEngineTests
{
    [Fact]
    public async Task ExecuteAsync_ActionExecutesSuccessfully_ReturnsPerRuleResults()
    {
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var executor = new DelegatingActionExecutor(
            "ok-action",
            (_, _, _) => Task.FromResult(ActionResult.Success("done")));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["ok-action"] = executor
        });
        var engine = new RuleEngine(evaluator, factory);
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
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var executor = new DelegatingActionExecutor(
            "known",
            (_, _, _) => Task.FromResult(ActionResult.Success("known-success")));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["known"] = executor
        });
        var engine = new RuleEngine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Name = "Rule A",
            EventType = "Event",
            Condition = new ValueOperand { Value = true },
            Actions =
            [
                new ActionDefinition { Type = "unknown" },
                new ActionDefinition { Type = "known" }
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
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var fail = new DelegatingActionExecutor(
            "fails",
            (_, _, _) => throw new InvalidOperationException("boom"));
        var ok = new DelegatingActionExecutor(
            "ok",
            (_, _, _) => Task.FromResult(ActionResult.Success("ok")));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["fails"] = fail,
            ["ok"] = ok
        });
        var engine = new RuleEngine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Name = "Rule A",
            EventType = "Event",
            Condition = new ValueOperand { Value = true },
            Actions =
            [
                new ActionDefinition { Type = "fails" },
                new ActionDefinition { Type = "ok" }
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
        var evaluator = new TestConditionEvaluator(conditionResult: true, actionConditionResult: false);
        var executor = new DelegatingActionExecutor(
            "ok",
            (_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["ok"] = executor
        });
        var engine = new RuleEngine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Name = "Rule A",
            EventType = "Event",
            Condition = new ValueOperand { Value = true },
            Actions =
            [
                new ActionDefinition
                {
                    Type = "ok",
                    Condition = new ValueOperand { Value = false }
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
        var evaluator = new TestConditionEvaluator(conditionResult: false);
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>());
        var engine = new RuleEngine(evaluator, factory);
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
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var executor = new DelegatingActionExecutor(
            "ok",
            (_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["ok"] = executor
        });
        var engine = new RuleEngine(evaluator, factory);
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
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var executor = new DelegatingActionExecutor(
            "cancellable",
            (_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(ActionResult.Success());
            });
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["cancellable"] = executor
        });
        var engine = new RuleEngine(evaluator, factory);
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
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var executor = new CountingActionExecutor(
            "scheduled",
            (_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["scheduled"] = executor
        });
        var engine = new RuleEngine(evaluator, factory, new FixedTimeProvider(fixedNow));
        var rule = new RuleDefinition
        {
            Name = "Rule A",
            EventType = "Event",
            Condition = new ValueOperand { Value = true },
            Actions =
            [
                new ActionDefinition
                {
                    Type = "scheduled",
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
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var executor = new CountingActionExecutor(
            "scheduled",
            (_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["scheduled"] = executor
        });
        var engine = new RuleEngine(evaluator, factory, new FixedTimeProvider(fixedNow));
        var current = new OrderWithDateTimeOffset { DueDate = dueDate };
        var rule = new RuleDefinition
        {
            Name = "Rule A",
            EventType = "Event",
            Condition = new ValueOperand { Value = true },
            Actions =
            [
                new ActionDefinition
                {
                    Type = "scheduled",
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
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var executor = new CountingActionExecutor(
            "ok",
            (_, _, _) => Task.FromResult(ActionResult.Success("executed")));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["ok"] = executor
        });
        var engine = new RuleEngine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Name = "Rule A",
            EventType = "Event",
            Condition = new ValueOperand { Value = true },
            Actions =
            [
                new ActionDefinition
                {
                    Type = "ok",
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
        var evaluator = new TestConditionEvaluator(conditionResult: true, actionConditionResult: false);
        var executor = new CountingActionExecutor(
            "scheduled",
            (_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["scheduled"] = executor
        });
        var engine = new RuleEngine(evaluator, factory);
        var rule = new RuleDefinition
        {
            Name = "Rule A",
            EventType = "Event",
            Condition = new ValueOperand { Value = true },
            Actions =
            [
                new ActionDefinition
                {
                    Type = "scheduled",
                    Condition = new ValueOperand { Value = false },
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
        var evaluator = new TestConditionEvaluator(conditionResult: true);
        var executor = new CountingActionExecutor(
            "scheduled",
            (_, _, _) => Task.FromResult(ActionResult.Success()));
        var factory = new TestActionExecutorFactory(new Dictionary<string, IActionExecutor>
        {
            ["scheduled"] = executor
        });
        var engine = new RuleEngine(evaluator, factory);
        var current = new { DueDate = "not-a-date" };
        var rule = new RuleDefinition
        {
            Name = "Rule A",
            EventType = "Event",
            Condition = new ValueOperand { Value = true },
            Actions =
            [
                new ActionDefinition
                {
                    Type = "scheduled",
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

    private static RuleDefinition CreateRule(string name, string actionType, int priority = 1) =>
        new()
        {
            Name = name,
            EventType = "Event",
            Priority = priority,
            Condition = new ValueOperand { Value = true },
            Actions = [new ActionDefinition { Type = actionType }]
        };

    private sealed class TestActionExecutorFactory(
        IReadOnlyDictionary<string, IActionExecutor> executors) : IActionExecutorFactory
    {
        public IReadOnlyCollection<string> ActionTypes => executors.Keys.ToArray();

        public bool TryGetExecutor(string type, out IActionExecutor? executor) =>
            executors.TryGetValue(type, out executor);
    }

    private sealed class DelegatingActionExecutor(
        string type,
        Func<ActionDefinition, EvaluationContext, CancellationToken, Task<ActionResult>> execute) : IActionExecutor
    {
        public string Type => type;

        public Task<ActionResult> ExecuteAsync(
            ActionDefinition action,
            EvaluationContext context,
            CancellationToken cancellationToken = default) =>
            execute(action, context, cancellationToken);
    }

    private sealed class CountingActionExecutor(
        string type,
        Func<ActionDefinition, EvaluationContext, CancellationToken, Task<ActionResult>> execute) : IActionExecutor
    {
        public int ExecutionCount { get; private set; }
        public string Type => type;

        public Task<ActionResult> ExecuteAsync(
            ActionDefinition action,
            EvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return execute(action, context, cancellationToken);
        }
    }

    private sealed class TestConditionEvaluator(bool conditionResult, bool actionConditionResult = true)
        : IConditionEvaluator
    {
        public bool Evaluate(EvaluationContext context, ConditionNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            return node is ValueOperand { Value: false } ? actionConditionResult : conditionResult;
        }

    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class OrderWithDateTimeOffset
    {
        public DateTimeOffset DueDate { get; init; }
    }
}

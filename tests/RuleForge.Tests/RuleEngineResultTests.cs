using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Abstractions.Scheduling;
using RuleForge.Actions;
using RuleForge.Engine;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

public class RuleEngineResultTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly OperatorCondition Always = Op("eq", Value(1), Value(1));
    private static readonly OperatorCondition Never = Op("eq", Value(1), Value(2));
    private static readonly OperatorCondition Broken = Op("no-such-operator");

    private readonly RecordingAction _ok = new("ok");
    private readonly RuleEngine _engine;

    public RuleEngineResultTests()
    {
        _engine = new RuleEngine(Evaluator(), new DirectActionExecutorFactory([_ok]), new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task ExecuteAsync_EachRuleOutcome_ReportsStatusAndIdentity()
    {
        RuleDefinition[] rules =
        [
            Rule("Matches", Always, id: "r1"),
            Rule("Doesn't match", Never, id: "r2"),
            Rule("Broken", Broken, id: "r3")
        ];

        var result = await Execute(rules);

        Assert.Collection(result.Rules,
            r =>
            {
                Assert.Equal(("r1", "Matches", RuleExecutionStatus.Matched), (r.RuleId, r.RuleName, r.Status));
                Assert.Null(r.Error);
                Assert.Single(r.ActionResults);
            },
            r =>
            {
                Assert.Equal(("r2", "Doesn't match", RuleExecutionStatus.NotMatched), (r.RuleId, r.RuleName, r.Status));
                Assert.Null(r.Error);
                Assert.Empty(r.ActionResults);
            },
            r =>
            {
                Assert.Equal(("r3", "Broken", RuleExecutionStatus.Failed), (r.RuleId, r.RuleName, r.Status));
                Assert.IsType<InvalidOperationException>(r.Error);
                Assert.Empty(r.ActionResults);
            });
        Assert.Equal(1, _ok.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RuleConditionFails_LaterRulesStillRun()
    {
        var result = await Execute([Rule("Broken", Broken, priority: 10), Rule("Fine", Always, priority: 1)]);

        Assert.Equal([RuleExecutionStatus.Failed, RuleExecutionStatus.Matched], result.Rules.Select(r => r.Status));
        Assert.Equal(1, _ok.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ActionConditionFails_FailsOnlyThatActionAndKeepsOthers()
    {
        var rule = Rule("Mixed", Always, actions:
        [
            Action("ok"),
            Action("ok", condition: Broken),
            Action("ok")
        ]);

        var result = await Execute([rule]);

        var ruleResult = Assert.Single(result.Rules);
        Assert.Equal(RuleExecutionStatus.Matched, ruleResult.Status);
        Assert.Equal(
            [ActionExecutionStatus.ExecutedImmediately, ActionExecutionStatus.Failed, ActionExecutionStatus.ExecutedImmediately],
            ruleResult.ActionResults.Select(a => a.Status));
        Assert.IsType<InvalidOperationException>(ruleResult.ActionResults[1].Error);
        Assert.Equal(2, _ok.Calls);
        Assert.True(result.HasFailures);
    }

    [Fact]
    public async Task ExecuteAsync_ActionResults_AreStampedWithTypeAndIndex()
    {
        var spoofing = new RecordingAction("spoofing", ActionResult.Success() with { ActionType = "other", ActionIndex = 99 });
        var engine = new RuleEngine(Evaluator(), new DirectActionExecutorFactory([_ok, spoofing]));
        var rule = Rule("Identity", Always,
            actions: [Action("ok"), Action("missing"), Action("spoofing"), Action("ok", condition: Never)]);

        var result = await engine.ExecuteAsync("Event", new { }, [rule], TestContext.Current.CancellationToken);

        Assert.Equal(
            [("ok", 0), ("missing", 1), ("spoofing", 2), ("ok", 3)],
            result.Rules[0].ActionResults.Select(a => (a.ActionType, a.ActionIndex)));
    }

    [Fact]
    public async Task ExecuteAsync_ExecutorReturnsNull_FailsAction()
    {
        var engine = new RuleEngine(Evaluator(), new DirectActionExecutorFactory([new RecordingAction("null-result", returnsNull: true)]));

        var result = await engine.ExecuteAsync("Event", new { }, [Rule("Null", Always, actions: [Action("null-result")])],
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Rules[0].ActionResults);
        Assert.Equal(ActionExecutionStatus.Failed, action.Status);
        Assert.IsType<InvalidOperationException>(action.Error);
    }

    [Fact]
    public async Task ExecuteAsync_NoFailures_HasFailuresIsFalse()
    {
        var result = await Execute([Rule("Fine", Always), Rule("Skipped", Never), Rule("Skipped action", Always, actions: [Action("ok", condition: Never)])]);

        Assert.False(result.HasFailures);
    }

    [Fact]
    public async Task ExecuteAsync_RuleFailure_HasFailuresIsTrue()
    {
        var result = await Execute([Rule("Broken", Broken)]);

        Assert.True(result.HasFailures);
    }

    [Fact]
    public async Task ExecuteAsync_ScheduledActions_ListsEveryScheduledActionWithItsRuleAndDefinition()
    {
        var delayed = Action("ok", schedule: new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 2, Unit = ScheduleUnit.Hours });
        var advanced = Action("ok", schedule: new ScheduleMetadata { Mode = ScheduleMode.Advance, Amount = 1, Unit = ScheduleUnit.Days });
        RuleDefinition[] rules =
        [
            Rule("First", Always, priority: 2, id: "r1", actions: [Action("ok"), delayed]),
            Rule("Second", Always, priority: 1, id: "r2", actions: [advanced])
        ];

        var result = await Execute(rules);

        Assert.Collection(result.ScheduledActions,
            s =>
            {
                Assert.Equal(("r1", "First"), (s.RuleId, s.RuleName));
                Assert.Same(delayed, s.Action);
                Assert.Equal(Now.AddHours(2), s.Result.ScheduledFor);
                Assert.Equal(1, s.Result.ActionIndex);
            },
            s =>
            {
                Assert.Equal(("r2", "Second"), (s.RuleId, s.RuleName));
                Assert.Same(advanced, s.Action);
                Assert.Equal(Now.AddDays(-1), s.Result.ScheduledFor);
            });
        Assert.Equal(1, _ok.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RulesForOtherEvents_AreNotIncluded()
    {
        var result = await _engine.ExecuteAsync(
            "Event", new { },
            [Rule("This event", Always), new RuleDefinition { Name = "Other", EventType = "OtherEvent", Condition = Always }],
            TestContext.Current.CancellationToken);

        Assert.Equal(["This event"], result.Rules.Select(r => r.RuleName));
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeStart_ThrowsWithoutRunningActions()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _engine.ExecuteAsync("Event", new { }, [Rule("Fine", Always)], cts.Token));
        Assert.Equal(0, _ok.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBetweenActions_StopsBeforeNextAction()
    {
        using var cts = new CancellationTokenSource();
        var cancelling = new RecordingAction("cancel", ActionResult.Success(), onExecute: cts.Cancel);
        var engine = new RuleEngine(Evaluator(), new DirectActionExecutorFactory([_ok, cancelling]));
        var rule = Rule("Cancelled midway", Always, actions: [Action("cancel"), Action("ok")]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.ExecuteAsync("Event", new { }, [rule], cts.Token));
        Assert.Equal(1, cancelling.Calls);
        Assert.Equal(0, _ok.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_Failures_AreNotWrittenToConsole()
    {
        var originalError = Console.Error;
        using var captured = new StringWriter();
        Console.SetError(captured);

        try
        {
            await Execute([Rule("Broken", Broken), Rule("Broken action", Always, actions: [Action("missing")])]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(string.Empty, captured.ToString());
    }

    private Task<RuleEngineResult> Execute(RuleDefinition[] rules) =>
        _engine.ExecuteAsync("Event", new { }, rules, TestContext.Current.CancellationToken);

    /// <summary>A rule for "Event"; without explicit actions it has a single "ok" action.</summary>
    private static RuleDefinition Rule(
        string name,
        ConditionNode condition,
        int priority = 0,
        string? id = null,
        ActionDefinition[]? actions = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name,
            EventType = "Event",
            Priority = priority,
            Condition = condition,
            Actions = [.. actions ?? [Action("ok")]]
        };

    private static ActionDefinition Action(string type, ConditionNode? condition = null, ScheduleMetadata? schedule = null) =>
        new() { Type = type, Condition = condition, Schedule = schedule };

    /// <summary>Counts executions and returns <paramref name="result"/> (default: success), or null when <paramref name="returnsNull"/>.</summary>
    private sealed class RecordingAction(
        string type,
        ActionResult? result = null,
        Action? onExecute = null,
        bool returnsNull = false) : IActionExecutor
    {
        public int Calls { get; private set; }
        public string Type => type;

        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            onExecute?.Invoke();
            return Task.FromResult(returnsNull ? null! : result ?? ActionResult.Success());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

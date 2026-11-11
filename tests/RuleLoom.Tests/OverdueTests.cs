using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Engine;
using RuleLoom.Validation;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class OverdueTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CountingAction _remind = new();

    [Fact]
    public async Task ExecuteAsync_DueTimeAlreadyPassed_RunsTheActionNow()
    {
        var result = await Engine().ExecuteAsync("OrderPlaced", new { DueAt = Now.AddHours(1) }, [Rule(advanceHours: 3)], TestContext.Current.CancellationToken);

        Assert.Equal(ActionExecutionStatus.Executed, Single(result).Status);
        Assert.Empty(result.ScheduledActions);
        Assert.Equal(1, _remind.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_DueNowOrLater_IsScheduled()
    {
        var result = await Engine().ExecuteAsync("OrderPlaced", new { DueAt = Now.AddHours(3) }, [Rule(advanceHours: 3)], TestContext.Current.CancellationToken);

        Assert.Equal((ActionExecutionStatus.Scheduled, Now), (Single(result).Status, Single(result).ScheduledFor));
        Assert.Equal(0, _remind.Calls);
    }

    [Theory]
    [InlineData(3, ActionExecutionStatus.Skipped)]       // due 3 hours ago
    [InlineData(0.4, ActionExecutionStatus.Executed)]    // due 24 minutes ago: within the grace period
    public async Task ExecuteAsync_SkippingOverdueActions_SkipsThoseLaterThanTheGracePeriod(double advanceHours, ActionExecutionStatus expected)
    {
        var engine = Engine(new RuleEngineOptions { SkipOverdueActions = true, OverdueGracePeriod = TimeSpan.FromMinutes(30) });

        var result = await engine.ExecuteAsync("OrderPlaced", new { DueAt = Now }, [Rule(advanceHours)], TestContext.Current.CancellationToken);

        Assert.Equal(expected, Single(result).Status);
    }

    [Fact]
    public async Task ExecuteAsync_OverdueSkipped_SaysByHowMuch()
    {
        var engine = Engine(new RuleEngineOptions { SkipOverdueActions = true, OverdueGracePeriod = TimeSpan.FromMinutes(30) });

        var result = await engine.ExecuteAsync("OrderPlaced", new { DueAt = Now }, [Rule(advanceHours: 3)], TestContext.Current.CancellationToken);

        Assert.Equal("Action 'remind' skipped: overdue by 3 hours (grace period 30 minutes).", Single(result).Message);
        Assert.Equal(0, _remind.Calls);
    }

    [Theory]
    [InlineData(false, 0, ActionExecutionStatus.Executed)]   // not skipping: runs, however late
    [InlineData(true, 30, ActionExecutionStatus.Skipped)]    // 3 hours late
    [InlineData(true, 240, ActionExecutionStatus.Executed)]  // within a 4-hour grace period
    public async Task ExecuteScheduledAsync_RunLate_IsSkippedOnlyWhenOverdue(bool skip, int graceMinutes, ActionExecutionStatus expected)
    {
        var engine = Engine(new RuleEngineOptions { SkipOverdueActions = skip, OverdueGracePeriod = TimeSpan.FromMinutes(graceMinutes) });
        var scheduled = new ScheduledAction("reminder", 0, "remind", Now.AddHours(-3), null);

        var result = await engine.ExecuteScheduledAsync(scheduled, new RuleEvent("OrderPlaced", new { DueAt = Now }), [Rule(advanceHours: 1)],
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Options_NegativeGracePeriod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleEngineOptions { OverdueGracePeriod = TimeSpan.FromMinutes(-1) });
    }

    [Fact]
    public void Validate_AdvanceFromNow_IsAWarning_AndAmountMustBePositive()
    {
        var validator = new RuleValidator(new TestCatalog().Action("remind", new CountingAction()).Event("OrderPlaced").Build());
        var rule = new RuleDefinition
        {
            Id = "r", Name = "R", EventType = "OrderPlaced", Condition = Op("eq", Value(1), Value(1)),
            Actions =
            [
                new ActionDefinition { Action = "remind", Schedule = new ScheduleMetadata { Mode = ScheduleMode.Advance, Amount = 1, Unit = ScheduleUnit.Hours } },
                new ActionDefinition { Action = "remind", Schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 0, Unit = ScheduleUnit.Hours } }
            ]
        };

        var result = validator.Validate(rule);

        Assert.Equal(new RuleValidationError("$.actions[0].schedule",
            "'Advance' from now is always in the past, so the action runs immediately; did you mean to set a 'referencePath'?"), Assert.Single(result.Warnings));
        Assert.Equal(new RuleValidationError("$.actions[1].schedule.amount", "'amount' must be greater than zero, not 0."), Assert.Single(result.Errors));
    }

    private RuleEngine Engine(RuleEngineOptions? options = null) =>
        new(new TestCatalog().Action("remind", _remind).Build(), new TestCatalog().Action("remind", _remind), timeProvider: new FixedTimeProvider(Now), options: options);

    // Remind advanceHours before DueAt.
    private static RuleDefinition Rule(double advanceHours) => new()
    {
        Id = "reminder", Name = "Reminder", EventType = "OrderPlaced", Condition = Op("eq", Value(1), Value(1)),
        Actions =
        [
            new ActionDefinition
            {
                Action = "remind",
                Schedule = new ScheduleMetadata
                {
                    Mode = ScheduleMode.Advance, Amount = (int)Math.Round(advanceHours * 60), Unit = ScheduleUnit.Minutes, ReferencePath = "DueAt"
                }
            }
        ]
    };

    private static ActionResult Single(RuleEngineResult result) => Assert.Single(Assert.Single(result.Rules).ActionResults);

    private sealed class CountingAction : IAction
    {
        public int Calls { get; private set; }

        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(ActionResult.Success());
        }
    }
}

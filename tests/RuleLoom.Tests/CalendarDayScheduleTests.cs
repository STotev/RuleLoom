using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

// Europe/Sofia in 2026: +02:00 until 29 March 03:00 (clocks to 04:00, +03:00); back from 04:00 to 03:00 on 25 October.
public class CalendarDayScheduleTests
{
    [Theory]
    [InlineData("2026-03-29T09:00", ScheduleMode.Advance, 1, ScheduleUnit.Days, "2026-03-28T07:00:00Z")]    // 09:00 the day before (+02:00)
    [InlineData("2026-10-24T09:00", ScheduleMode.Delay, 1, ScheduleUnit.Days, "2026-10-25T07:00:00Z")]      // 09:00 the day after (+02:00)
    [InlineData("2026-03-28T09:00", ScheduleMode.Delay, 24, ScheduleUnit.Hours, "2026-03-29T07:00:00Z")]    // elapsed: 10:00 by the clock
    [InlineData("2026-03-28T03:30", ScheduleMode.Delay, 1, ScheduleUnit.Days, "2026-03-29T01:30:00Z")]      // 03:30 is skipped: 04:30
    [InlineData("2026-10-24T03:30", ScheduleMode.Delay, 1, ScheduleUnit.Days, "2026-10-25T00:30:00Z")]      // 03:30 twice: the earlier
    [InlineData("2026-03-29", ScheduleMode.Advance, 1, ScheduleUnit.Days, "2026-03-27T22:00:00Z")]          // a date: from its midnight
    public async Task Schedule_DaysAreCalendarDaysInTheTimeZone_OtherUnitsElapsedTime(
        string reference, ScheduleMode mode, int amount, ScheduleUnit unit, string expected)
    {
        var schedule = new ScheduleMetadata { Mode = mode, Amount = amount, Unit = unit, ReferencePath = "DueAt" };

        Assert.Equal(DateTimeOffset.Parse(expected), await ScheduleAsync(schedule, new { DueAt = reference }));
    }

    [Fact]
    public async Task Schedule_FromNow_InCalendarDays()
    {
        var schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 1, Unit = ScheduleUnit.Days };

        // Now is 09:00 on 24 October (+03:00); a day later is 09:00 on 25 October (+02:00).
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 7, 0, 0, TimeSpan.Zero),
            await ScheduleAsync(schedule, new { }, now: new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Schedule_UtcCatalog_DaysAre24Hours()
    {
        var schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 2, Unit = ScheduleUnit.Days, ReferencePath = "DueAt" };

        Assert.Equal(new DateTimeOffset(2026, 3, 30, 9, 0, 0, TimeSpan.Zero),
            await ScheduleAsync(schedule, new { DueAt = "2026-03-28T09:00" }, timeZone: null));
    }

    // Now is before every due time here, so the actions are scheduled rather than run.
    private static async Task<DateTimeOffset> ScheduleAsync(ScheduleMetadata schedule, object state, string? timeZone = "Europe/Sofia",
        DateTimeOffset? now = null)
    {
        var catalog = new TestCatalog().Action("remind", new NoOpAction());
        if (timeZone is not null)
            catalog.TimeZone(timeZone);

        var rule = new RuleDefinition
        {
            Id = "r", Name = "R", EventType = "OrderPlaced", Condition = Op("eq", Value(1), Value(1)),
            Actions = [new ActionDefinition { Action = "remind", Schedule = schedule }]
        };

        var result = await catalog.Engine(new FixedTimeProvider(now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .ExecuteAsync("OrderPlaced", state, [rule], TestContext.Current.CancellationToken);

        var action = Assert.Single(Assert.Single(result.Rules).ActionResults);
        return action.ScheduledFor ?? throw new InvalidOperationException($"Not scheduled: {action.Status} {action.Message}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoOpAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}

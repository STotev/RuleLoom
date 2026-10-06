using RuleForge.Abstractions;
using RuleForge.Abstractions.Scheduling;
using RuleForge.Scheduling;

namespace RuleForge.Tests;

public class ScheduleCalculatorTests
{
    [Theory]
    [InlineData(ScheduleMode.Delay, ScheduleUnit.Seconds, 30)]
    [InlineData(ScheduleMode.Delay, ScheduleUnit.Minutes, 2)]
    [InlineData(ScheduleMode.Delay, ScheduleUnit.Hours, 24)]
    [InlineData(ScheduleMode.Delay, ScheduleUnit.Days, 7)]
    [InlineData(ScheduleMode.Advance, ScheduleUnit.Seconds, 30)]
    [InlineData(ScheduleMode.Advance, ScheduleUnit.Minutes, 2)]
    [InlineData(ScheduleMode.Advance, ScheduleUnit.Hours, 2)]
    [InlineData(ScheduleMode.Advance, ScheduleUnit.Days, 1)]
    public void Calculate_ModeAndUnitCombination_ReturnsExpectedTimestamp(
        ScheduleMode mode,
        ScheduleUnit unit,
        int amount)
    {
        var utcNow = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var context = new EvaluationContext(new { });
        var schedule = new ScheduleMetadata
        {
            Mode = mode,
            Amount = amount,
            Unit = unit
        };

        var result = ScheduleCalculator.Calculate(schedule, context, utcNow);

        var delta = unit switch
        {
            ScheduleUnit.Seconds => TimeSpan.FromSeconds(amount),
            ScheduleUnit.Minutes => TimeSpan.FromMinutes(amount),
            ScheduleUnit.Hours => TimeSpan.FromHours(amount),
            ScheduleUnit.Days => TimeSpan.FromDays(amount),
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        var expected = mode switch
        {
            ScheduleMode.Delay => utcNow + delta,
            ScheduleMode.Advance => utcNow - delta,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        Assert.Equal(expected, result);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void Calculate_ReferencePathResolvesDateTime_UsesReferenceValue()
    {
        var utcNow = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var dueDate = new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc);
        var context = new EvaluationContext(new OrderWithDateTime { DueDate = dueDate });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Advance,
            Amount = 2,
            Unit = ScheduleUnit.Hours,
            ReferencePath = "DueDate"
        };

        var result = ScheduleCalculator.Calculate(schedule, context, utcNow);

        Assert.Equal(new DateTimeOffset(dueDate).AddHours(-2), result);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void Calculate_ReferencePathResolvesDateTimeOffset_UsesReferenceValue()
    {
        var utcNow = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var dueDate = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
        var context = new EvaluationContext(new OrderWithDateTimeOffset { DueDate = dueDate });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Delay,
            Amount = 90,
            Unit = ScheduleUnit.Minutes,
            ReferencePath = "DueDate"
        };

        var result = ScheduleCalculator.Calculate(schedule, context, utcNow);

        Assert.Equal(dueDate.AddMinutes(90), result);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void Calculate_NestedReferencePathWithoutScope_UsesReferenceValue()
    {
        var utcNow = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var dueDate = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
        var context = new EvaluationContext(new { Order = new OrderWithDateTimeOffset { DueDate = dueDate } });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Advance,
            Amount = 2,
            Unit = ScheduleUnit.Hours,
            ReferencePath = "Order.DueDate"
        };

        var result = ScheduleCalculator.Calculate(schedule, context, utcNow);

        Assert.Equal(dueDate.AddHours(-2), result);
    }

    [Fact]
    public void Calculate_AdvanceBeforeReference_ResultCanBePast()
    {
        var utcNow = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var dueDate = utcNow.AddHours(2);
        var context = new EvaluationContext(new OrderWithDateTimeOffset { DueDate = dueDate });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Advance,
            Amount = 5,
            Unit = ScheduleUnit.Hours,
            ReferencePath = "DueDate"
        };

        var result = ScheduleCalculator.Calculate(schedule, context, utcNow);

        Assert.Equal(utcNow.AddHours(-3), result);
    }

    [Fact]
    public void Calculate_ReferencePathMissing_ThrowsInvalidOperationException()
    {
        var context = new EvaluationContext(new { });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Delay,
            Amount = 1,
            Unit = ScheduleUnit.Hours,
            ReferencePath = "MissingDate"
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ScheduleCalculator.Calculate(schedule, context, DateTimeOffset.UtcNow));

        Assert.Contains("resolved to null", ex.Message);
    }

    [Fact]
    public void Calculate_ReferencePathResolvesStringDateTime_ParsesAsUtc()
    {
        var utcNow = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);
        var context = new EvaluationContext(new { DueDate = "2026-04-02T10:00:00Z" });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Delay,
            Amount = 30,
            Unit = ScheduleUnit.Minutes,
            ReferencePath = "DueDate"
        };

        var result = ScheduleCalculator.Calculate(schedule, context, utcNow);

        Assert.Equal(new DateTimeOffset(2026, 4, 2, 10, 30, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void Calculate_ReferencePathNonIsoString_ThrowsInvalidOperationException()
    {
        var context = new EvaluationContext(new { DueDate = "not-a-date" });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Delay,
            Amount = 1,
            Unit = ScheduleUnit.Hours,
            ReferencePath = "DueDate"
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ScheduleCalculator.Calculate(schedule, context, DateTimeOffset.UtcNow));

        Assert.Contains("is not an ISO 8601 date-time", ex.Message);
    }

    [Theory]
    [InlineData("2026-04-02T10:00:00Z", 10)]
    [InlineData("2026-04-02T10:00:00", 10)]
    [InlineData("2026-04-02T12:00:00+02:00", 10)]
    [InlineData("2026-04-02T10:00", 10)]
    [InlineData("2026-04-02", 0)]
    public void Calculate_ReferencePathIsoString_UsesUtcInstant(string dueDate, int expectedHour)
    {
        var context = new EvaluationContext(new { DueDate = dueDate });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Delay,
            Amount = 1,
            Unit = ScheduleUnit.Hours,
            ReferencePath = "DueDate"
        };

        var result = ScheduleCalculator.Calculate(schedule, context, DateTimeOffset.UtcNow);

        Assert.Equal(new DateTimeOffset(2026, 4, 2, expectedHour + 1, 0, 0, TimeSpan.Zero), result);
    }

    [Theory]
    [InlineData("2026-04-02 10:00:00")]
    [InlineData("04/02/2026")]
    [InlineData("2 April 2026")]
    [InlineData(" 2026-04-02T10:00:00Z")]
    public void Calculate_ReferencePathNonIsoString_IsRejected(string dueDate)
    {
        var context = new EvaluationContext(new { DueDate = dueDate });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Delay,
            Amount = 1,
            Unit = ScheduleUnit.Hours,
            ReferencePath = "DueDate"
        };

        Assert.Throws<InvalidOperationException>(
            () => ScheduleCalculator.Calculate(schedule, context, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Calculate_ReferencePathUnsupportedType_ThrowsInvalidOperationException()
    {
        var context = new EvaluationContext(new { DueDate = 20260402 });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Delay,
            Amount = 1,
            Unit = ScheduleUnit.Hours,
            ReferencePath = "DueDate"
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ScheduleCalculator.Calculate(schedule, context, DateTimeOffset.UtcNow));

        Assert.Contains("resolved to 'Int32'", ex.Message);
    }

    [Fact]
    public void Calculate_AmountNull_ThrowsInvalidOperationException()
    {
        var context = new EvaluationContext(new { });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Delay,
            Amount = null,
            Unit = ScheduleUnit.Hours
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ScheduleCalculator.Calculate(schedule, context, DateTimeOffset.UtcNow));

        Assert.Contains("Schedule.Amount is required", ex.Message);
    }

    [Fact]
    public void Calculate_ImmediateMode_ThrowsArgumentException()
    {
        var context = new EvaluationContext(new { });
        var schedule = new ScheduleMetadata
        {
            Mode = ScheduleMode.Immediate,
            Amount = 1,
            Unit = ScheduleUnit.Hours
        };

        Assert.Throws<ArgumentException>(
            () => ScheduleCalculator.Calculate(schedule, context, DateTimeOffset.UtcNow));
    }

    private sealed class OrderWithDateTime
    {
        public DateTime DueDate { get; init; }
    }

    private sealed class OrderWithDateTimeOffset
    {
        public DateTimeOffset DueDate { get; init; }
    }
}

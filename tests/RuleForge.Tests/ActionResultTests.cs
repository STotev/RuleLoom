using RuleForge.Abstractions.Action;

namespace RuleForge.Tests;

public class ActionResultTests
{
    [Fact]
    public void Success_DefaultFactory_ReturnsExecutedImmediately()
    {
        var result = ActionResult.Success();

        Assert.Equal(ActionExecutionStatus.ExecutedImmediately, result.Status);
        Assert.Null(result.ScheduledFor);
        Assert.Null(result.Message);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Scheduled_WithDate_ReturnsScheduledStatusAndDate()
    {
        var scheduledFor = new DateTime(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc);

        var result = ActionResult.Scheduled(scheduledFor);

        Assert.Equal(ActionExecutionStatus.Scheduled, result.Status);
        Assert.Equal(scheduledFor, result.ScheduledFor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failed_WithException_ReturnsFailedStatusAndError()
    {
        var exception = new InvalidOperationException("boom");

        var result = ActionResult.Failed(exception);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Same(exception, result.Error);
        Assert.Null(result.ScheduledFor);
    }

    [Fact]
    public void Skipped_DefaultFactory_ReturnsSkippedStatus()
    {
        var result = ActionResult.Skipped();

        Assert.Equal(ActionExecutionStatus.Skipped, result.Status);
        Assert.Null(result.ScheduledFor);
        Assert.Null(result.Error);
    }
}

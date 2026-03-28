namespace RuleForge.Abstractions.Action;

/// <summary>
/// Represents the result of action execution.
/// </summary>
public sealed record ActionResult
{
    private static readonly ActionResult _success = new() { Status = ActionExecutionStatus.ExecutedImmediately };
    /// <summary>
    /// Execution status.
    /// </summary>
    public ActionExecutionStatus Status { get; init; }

    /// <summary>
    /// Scheduled execution timestamp in UTC when status is Scheduled.
    /// </summary>
    public DateTime? ScheduledFor { get; init; }

    /// <summary>
    /// Optional informational message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional error when execution failed.
    /// </summary>
    public Exception? Error { get; init; }

    public static ActionResult Success(string? message = null)
    {
        if (message is null) return _success;
        return new ActionResult
        {
            Status = ActionExecutionStatus.ExecutedImmediately,
            Message = message
        };
    }

    public static ActionResult Scheduled(DateTime scheduledFor, string? message = null)
    {
        return new ActionResult
        {
            Status = ActionExecutionStatus.Scheduled,
            ScheduledFor = scheduledFor,
            Message = message
        };
    }

    public static ActionResult Skipped(string? message = null)
    {
        return new ActionResult
        {
            Status = ActionExecutionStatus.Skipped,
            Message = message
        };
    }

    public static ActionResult Failed(Exception error, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ActionResult
        {
            Status = ActionExecutionStatus.Failed,
            Error = error,
            Message = message
        };
    }
}

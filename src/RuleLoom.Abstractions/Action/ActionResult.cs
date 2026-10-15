namespace RuleLoom.Abstractions.Action;

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
    public DateTimeOffset? ScheduledFor { get; init; }

    /// <summary>
    /// Optional informational message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional error when execution failed.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// The <see cref="ActionDefinition.Action"/> name of the action this result belongs to. Set by the rule engine.
    /// </summary>
    public string ActionName { get; init; } = string.Empty;

    /// <summary>
    /// The position of the action in its rule's <c>Actions</c> list. Set by the rule engine.
    /// </summary>
    public int ActionIndex { get; init; }

    public static ActionResult Success(string? message = null)
    {
        if (message is null) return _success;
        return new ActionResult
        {
            Status = ActionExecutionStatus.ExecutedImmediately,
            Message = message
        };
    }

    public static ActionResult Scheduled(DateTimeOffset scheduledFor, string? message = null)
    {
        return new ActionResult
        {
            Status = ActionExecutionStatus.Scheduled,
            ScheduledFor = scheduledFor.ToUniversalTime(),
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

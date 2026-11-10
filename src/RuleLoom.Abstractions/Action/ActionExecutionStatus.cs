namespace RuleLoom.Abstractions.Action;

/// <summary>
/// Represents the outcome status of an action execution request.
/// </summary>
public enum ActionExecutionStatus
{
    /// <summary>The action ran: right away, or later when it was due.</summary>
    Executed,

    /// <summary>The action is due later (<see cref="ActionResult.ScheduledFor"/>); the app's scheduler runs it.</summary>
    Scheduled,

    /// <summary>The action didn't run: its condition didn't hold.</summary>
    Skipped,

    /// <summary>The action, or something it needed, failed (<see cref="ActionResult.Error"/>).</summary>
    Failed
}

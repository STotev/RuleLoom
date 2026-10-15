namespace RuleLoom.Abstractions.Action;

/// <summary>
/// Represents the outcome status of an action execution request.
/// </summary>
public enum ActionExecutionStatus
{
    ExecutedImmediately,
    Scheduled,
    Skipped,
    Failed
}

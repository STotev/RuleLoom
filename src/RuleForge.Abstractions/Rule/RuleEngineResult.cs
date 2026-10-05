using RuleForge.Abstractions.Action;

namespace RuleForge.Abstractions.Rule;

/// <summary>
/// The outcome of processing one event: a result for every rule whose event type matched.
/// </summary>
public sealed record RuleEngineResult
{
    /// <summary>
    /// One result per rule whose event type matched, in execution order (priority, descending).
    /// </summary>
    public IReadOnlyList<RuleExecutionResult> Rules { get; init; } = [];

    /// <summary>
    /// True if any rule or any action failed.
    /// </summary>
    public bool HasFailures => Rules.Any(rule =>
        rule.Status == RuleExecutionStatus.Failed ||
        rule.ActionResults.Any(action => action.Status == ActionExecutionStatus.Failed));

    /// <summary>
    /// Every scheduled action across all rules, in execution order.
    /// </summary>
    public IReadOnlyList<ScheduledAction> ScheduledActions { get; init; } = [];
}

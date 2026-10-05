using RuleForge.Abstractions.Action;

namespace RuleForge.Abstractions.Rule;

/// <summary>
/// Represents the execution result for a single rule whose event type matched.
/// </summary>
public sealed record RuleExecutionResult
{
    /// <summary>
    /// The <see cref="RuleDefinition.Id"/> of the rule.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// The <see cref="RuleDefinition.Name"/> of the rule.
    /// </summary>
    public required string RuleName { get; init; }

    /// <summary>
    /// Whether the rule's condition matched, didn't match, or failed to evaluate.
    /// A rule whose actions failed is still <see cref="RuleExecutionStatus.Matched"/>; see its <see cref="ActionResults"/>.
    /// </summary>
    public RuleExecutionStatus Status { get; init; }

    /// <summary>
    /// The exception that made the rule fail, when <see cref="Status"/> is <see cref="RuleExecutionStatus.Failed"/>.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// One result per action, in definition order. Empty unless the rule matched.
    /// </summary>
    public IReadOnlyList<ActionResult> ActionResults { get; init; } = [];
}

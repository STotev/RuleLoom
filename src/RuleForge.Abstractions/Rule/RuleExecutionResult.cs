using RuleForge.Abstractions.Action;

namespace RuleForge.Abstractions.Rule;

/// <summary>
/// Represents the aggregate execution result for a rule.
/// </summary>
public sealed record RuleExecutionResult
{
    /// <summary>
    /// Action results produced during rule execution.
    /// </summary>
    public IReadOnlyList<ActionResult> ActionResults { get; init; } = [];
}

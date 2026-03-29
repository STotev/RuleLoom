using RuleForge.Abstractions.Action;

namespace RuleForge.Abstractions.Rule;

/// <summary>
/// Represents the execution result for a single rule.
/// </summary>
public sealed record RuleExecutionResult
{
    /// <summary>
    /// Rule name that produced this execution result.
    /// </summary>
    public string RuleName { get; init; } = string.Empty;

    /// <summary>
    /// Action results produced during this rule execution.
    /// </summary>
    public IReadOnlyList<ActionResult> ActionResults { get; init; } = [];
}

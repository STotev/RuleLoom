namespace RuleForge.Abstractions.Condition;

/// <summary>
/// Represents a reference to a property path that will be resolved at evaluation time.
/// </summary>
public class PathOperand : ConditionNode
{
    /// <summary>
    /// The property path to resolve (e.g., "Price", "current.Price", "previous.Status").
    /// If no scope prefix is provided, defaults to "current".
    /// </summary>
    public required string Path { get; init; }
}

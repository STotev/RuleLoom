namespace RuleLoom.Abstractions.Condition;

/// <summary>
/// Represents a reference to a property path that will be resolved at evaluation time.
/// </summary>
public class PathOperand : ConditionNode
{
    /// <summary>
    /// The property path to resolve (e.g., "Price", "Address.City", "$current.Price", "$previous.Status").
    /// Without a <c>$current.</c> or <c>$previous.</c> prefix, the path resolves against the current state.
    /// </summary>
    public required string Path { get; init; }
}

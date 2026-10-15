namespace RuleLoom.Abstractions.Condition;

/// <summary>
/// Represents a constant value used in a condition.
/// </summary>
public class ValueOperand : ConditionNode
{
    /// <summary>
    /// The constant value. Can be any CLR type (string, int, bool, etc.).
    /// </summary>
    public object? Value { get; init; }
}

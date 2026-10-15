namespace RuleLoom.Abstractions.Condition;

/// <summary>
/// Represents a condition that uses an operator to evaluate one or more operands.
/// Operators can be nested to create complex condition trees.
/// </summary>
public class OperatorCondition : ConditionNode
{
    /// <summary>
    /// The name of the operator to use (e.g., "eq", "and", "changed").
    /// Must match an <see cref="IConditionOperator.Name"/> registered in the evaluator.
    /// </summary>
    public required string Operator { get; init; }
    
    /// <summary>
    /// The operands for this operator. Can contain path operands, value operands, or nested operators.
    /// </summary>
    public List<ConditionNode> Operands { get; init; } = [];
}
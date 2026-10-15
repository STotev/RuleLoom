namespace RuleLoom.Abstractions.Condition;

/// <summary>
/// Defines a condition operator that can be used in rule conditions.
/// </summary>
public interface IConditionOperator
{
    /// <summary>
    /// The name of the operator (e.g., "eq", "and", "changed").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The minimum number of operands required by this operator.
    /// </summary>
    int MinOperands { get; }

    /// <summary>
    /// The maximum number of operands allowed by this operator.
    /// Use int.MaxValue for unlimited operands.
    /// </summary>
    int MaxOperands { get; }

    /// <summary>
    /// Evaluates the operator. Read operands through <paramref name="context"/>:
    /// <see cref="OperatorContext.GetValue"/> for values, <see cref="OperatorContext.Test"/> for conditions,
    /// <see cref="OperatorContext.GetPath"/> for paths.
    /// </summary>
    bool Evaluate(OperatorContext context);
}

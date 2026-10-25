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
    /// A readable name for authoring UIs (e.g. "equals"); <c>null</c> when the operator doesn't give one.
    /// </summary>
    string? Label => null;

    /// <summary>
    /// A longer explanation of what the operator tests; <c>null</c> when the operator doesn't give one.
    /// </summary>
    string? Description => null;

    /// <summary>
    /// Evaluates the operator. Read operands through <paramref name="context"/>:
    /// <see cref="OperatorContext.GetValue"/> for values, <see cref="OperatorContext.Test"/> for conditions,
    /// <see cref="OperatorContext.GetPath"/> for paths.
    /// </summary>
    bool Evaluate(OperatorContext context);

    /// <summary>
    /// Checks the operands when a rule is validated, before it runs: e.g. that an operand is a path, or (with schemas)
    /// that compared types can match. Called only with an operand count within <see cref="MinOperands"/> and
    /// <see cref="MaxOperands"/>. Report problems through <paramref name="context"/>; by default there is nothing to check.
    /// </summary>
    void ValidateOperands(OperandValidationContext context)
    {
    }
}

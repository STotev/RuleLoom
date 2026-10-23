using RuleLoom.Abstractions.Condition;

namespace RuleLoom.Operators;

/// <summary>
/// True if the first operand is less than or equal to the second.
/// Values that can't be ordered (nulls, mismatched types) evaluate to <c>false</c>.
/// </summary>
public sealed class LessThanOrEqualOperator : IConditionOperator
{
    public string Name => "lte";
    public int MinOperands => 2;
    public int MaxOperands => 2;

    public bool Evaluate(OperatorContext context) =>
        context.TryCompare(context.GetValue(0), context.GetValue(1), out var result) && result <= 0;

    public void ValidateOperands(OperandValidationContext context) => ComparedOperands.Validate(context, ordering: true);
}

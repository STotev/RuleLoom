using RuleLoom.Abstractions.Condition;

namespace RuleLoom.Operators;

public class EqualsOperator : IConditionOperator
{
    public string Name => "eq";
    public string Label => "equals";
    public string Description => "True when the two operands are equal: numbers by value, dates as instants, text exactly.";
    public int MinOperands => 2;
    public int MaxOperands => 2;

    public bool Evaluate(OperatorContext context) =>
        context.AreEqual(context.GetValue(0), context.GetValue(1));

    public void ValidateOperands(OperandValidationContext context) => ComparedOperands.Validate(context, ordering: false);
}

using RuleLoom.Abstractions.Condition;

namespace RuleLoom.Operators;

public class NotOperator : IConditionOperator
{
    public string Name => "not";
    public string Label => "not";
    public string Description => "True when the operand is false.";
    public int MinOperands => 1;
    public int MaxOperands => 1;

    public bool Evaluate(OperatorContext context) => !context.Test(0);
}

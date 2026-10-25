using RuleLoom.Abstractions.Condition;

namespace RuleLoom.Operators;

public class AndOperator : IConditionOperator
{
    public string Name => "and";
    public string Label => "all of";
    public string Description => "True when every operand is true.";
    public int MinOperands => 1;
    public int MaxOperands => int.MaxValue;

    public bool Evaluate(OperatorContext context)
    {
        for (var i = 0; i < context.OperandCount; i++)
        {
            if (!context.Test(i))
                return false;
        }

        return true;
    }
}

using RuleLoom.Abstractions.Condition;

namespace RuleLoom.Operators;

public class OrOperator : IConditionOperator
{
    public string Name => "or";
    public int MinOperands => 1;
    public int MaxOperands => int.MaxValue;

    public bool Evaluate(OperatorContext context)
    {
        for (var i = 0; i < context.OperandCount; i++)
        {
            if (context.Test(i))
                return true;
        }

        return false;
    }
}

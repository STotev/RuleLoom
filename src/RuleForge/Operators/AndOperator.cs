using RuleForge.Abstractions.Condition;

namespace RuleForge.Operators;

public class AndOperator : IConditionOperator
{
    public string Name => "and";
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

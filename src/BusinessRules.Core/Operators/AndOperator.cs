using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Operators;

public class AndOperator : IConditionOperator
{
    public string Name => "and";
    public int MinOperands => 1;
    public int MaxOperands => int.MaxValue;

    public bool Evaluate(EvaluationContext ctx, IReadOnlyList<ConditionNode> operands, IConditionEvaluator evaluator)
    {
        foreach (var operand in operands)
        {
            if (!evaluator.Evaluate(ctx, operand))
                return false;
        }

        return true;
    }
}


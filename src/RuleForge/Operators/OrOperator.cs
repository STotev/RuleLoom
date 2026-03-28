using RuleForge.Abstractions;
using RuleForge.Abstractions.Condition;

namespace RuleForge.Operators;

public class OrOperator : IConditionOperator
{
    public string Name => "or";
    public int MinOperands => 1;
    public int MaxOperands => int.MaxValue;

    public bool Evaluate(EvaluationContext context, IReadOnlyList<ConditionNode> operands,
        IConditionEvaluator evaluator)
    {
        foreach (var operand in operands)
        {
            if (evaluator.Evaluate(context, operand))
            {
                return true;
            }
        }

        return false;
    }
}

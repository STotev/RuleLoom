using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Operators;

public class NotOperator : IConditionOperator
{
    public string Name => "not";
    public int MinOperands => 1;
    public int MaxOperands => 1;

    public bool Evaluate(EvaluationContext context, IReadOnlyList<ConditionNode> operands,
        IConditionEvaluator evaluator)
    {
        return !evaluator.Evaluate(context, operands[0]);
    }
}

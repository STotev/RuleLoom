using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Operators;

public class NotOperator : IConditionOperator
{
    public string Name => "not";

    public bool Evaluate(EvaluationContext context, IReadOnlyList<ConditionNode> operands,
        IConditionEvaluator evaluator)
    {
        return !evaluator.Evaluate(context, operands[0]);
    }
}

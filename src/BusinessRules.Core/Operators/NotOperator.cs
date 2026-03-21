using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;
using BusinessRules.Core.Helpers;

namespace BusinessRules.Core.Operators;

public class NotOperator : IOperator
{
    public string Name => "not";

    public object Evaluate(EvaluationContext context, IReadOnlyList<ConditionNode> operands,
        IExpressionEvaluator evaluator)
    {
        return !BoolHelper.ToBool(evaluator.Evaluate(context, operands[0]));
    }
}

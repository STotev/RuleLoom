using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;
using BusinessRules.Core.Helpers;

namespace BusinessRules.Core.Operators;

public class OrOperator : IOperator
{
    public string Name => "or";

    public object Evaluate(EvaluationContext context, IReadOnlyList<ConditionNode> operands,
        IExpressionEvaluator evaluator)
    {
        foreach (var operand in operands)
        {
            var result = evaluator.Evaluate(context, operand);
            if (BoolHelper.ToBool(result))
            {
                return true;
            }
        }

        return false;
    }
}

using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;
using BusinessRules.Core.Helpers;

namespace BusinessRules.Core.Operators;

public class AndOperator : IOperator
{
    public string Name => "and";

    public object Evaluate(EvaluationContext ctx, IReadOnlyList<ConditionNode> operands, IExpressionEvaluator evaluator)
    {
        foreach (var operand in operands)
        {
            var result = evaluator.Evaluate(ctx, operand);
            if (!BoolHelper.ToBool(result))
            {
                return false;
            }
        }

        return true;
    }
}

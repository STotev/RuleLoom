using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Operators;

public class EqualsOperator : IOperator
{
    public string Name => "eq";

    public object Evaluate(
        EvaluationContext ctx,
        IReadOnlyList<ConditionNode> operands,
        IExpressionEvaluator evaluator)
    {
        var left = evaluator.Evaluate(ctx, operands[0]);
        var right = evaluator.Evaluate(ctx, operands[1]);

        return Equals(left, right);
    }
}

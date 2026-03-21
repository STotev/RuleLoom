using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Operators;

public class EqualsOperator : IConditionOperator
{
    public string Name => "eq";
    public int MinOperands => 2;
    public int MaxOperands => 2;

    public bool Evaluate(
        EvaluationContext ctx,
        IReadOnlyList<ConditionNode> operands,
        IConditionEvaluator evaluator)
    {
        var left = evaluator.ResolveValue(ctx, operands[0]);
        var right = evaluator.ResolveValue(ctx, operands[1]);

        return Equals(left, right);
    }
}

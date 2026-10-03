using RuleForge.Abstractions;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Values;

namespace RuleForge.Operators;

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

        return ValueComparer.AreEqual(left, right);
    }
}

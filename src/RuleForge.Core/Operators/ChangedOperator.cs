using RuleForge.Abstractions;
using RuleForge.Abstractions.Condition;

namespace RuleForge.Core.Operators;

public class ChangedOperator : IConditionOperator
{
    public string Name => "changed";
    public int MinOperands => 1;
    public int MaxOperands => 1;

    public bool Evaluate(
        EvaluationContext context,
        IReadOnlyList<ConditionNode> operands,
        IConditionEvaluator evaluator)
    {
        var pathNode = operands[0] as PathOperand ??
                       throw new InvalidOperationException("changed expects a path");

        var current = context.Resolve($"current.{pathNode.Path}");
        var previous = context.Resolve($"previous.{pathNode.Path}");

        return !Equals(current, previous);
    }
}

using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Operators;

public class ChangedOperator : IConditionOperator
{
    public string Name => "changed";

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

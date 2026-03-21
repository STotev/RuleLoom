using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Abstractions;

public interface IOperator
{
    string Name { get; }

    object? Evaluate(EvaluationContext ctx, IReadOnlyList<ConditionNode> operands, IExpressionEvaluator evaluator);
}

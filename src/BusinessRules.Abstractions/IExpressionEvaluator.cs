using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Abstractions;

public interface IExpressionEvaluator
{
    object? Evaluate(EvaluationContext context, ConditionNode node);
}

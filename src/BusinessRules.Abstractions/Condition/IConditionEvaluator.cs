namespace BusinessRules.Abstractions.Condition;

public interface IConditionEvaluator
{
    bool Evaluate(EvaluationContext context, ConditionNode node);
    object? ResolveValue(EvaluationContext context, ConditionNode node);
}
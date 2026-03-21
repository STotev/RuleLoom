namespace BusinessRules.Abstractions.Condition;

public interface IConditionOperator
{
    string Name { get; }
    bool Evaluate(EvaluationContext context, IReadOnlyList<ConditionNode> operands, IConditionEvaluator evaluator);
}

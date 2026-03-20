namespace BusinessRules.Abstractions;

public interface IConditionOperator
{
    string Name { get; }
    bool Evaluate(EvaluationContext context, params object[] args);
}
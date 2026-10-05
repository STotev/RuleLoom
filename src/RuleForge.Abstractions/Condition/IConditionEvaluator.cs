namespace RuleForge.Abstractions.Condition;

/// <summary>
/// Evaluates condition nodes using registered operators.
/// </summary>
public interface IConditionEvaluator
{
    /// <summary>
    /// Evaluates a condition node and returns true if the condition is met.
    /// </summary>
    /// <param name="context">The evaluation context with current/previous state.</param>
    /// <param name="node">The condition node to evaluate; must be an <see cref="OperatorCondition"/>.</param>
    /// <returns>True if the condition is met, false otherwise.</returns>
    bool Evaluate(EvaluationContext context, ConditionNode node);
}

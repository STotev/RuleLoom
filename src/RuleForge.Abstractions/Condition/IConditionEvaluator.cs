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
    /// <param name="node">The condition node to evaluate.</param>
    /// <returns>True if the condition is met, false otherwise.</returns>
    bool Evaluate(EvaluationContext context, ConditionNode node);
    
    /// <summary>
    /// Resolves a condition node to its value. Handles path resolution, value operands, and nested operators.
    /// </summary>
    /// <param name="context">The evaluation context.</param>
    /// <param name="node">The node to resolve.</param>
    /// <returns>The resolved value, or null if the path doesn't exist.</returns>
    object? ResolveValue(EvaluationContext context, ConditionNode node);
}
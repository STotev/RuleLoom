namespace RuleForge.Abstractions.Action;

/// <summary>
/// Defines an executor for rule actions.
/// </summary>
public interface IActionExecutor
{
    /// <summary>
    /// The unique type identifier for this action (e.g., "log", "send-email").
    /// Used for action resolution in rules.
    /// </summary>
    string Type { get; }
    
    /// <summary>
    /// Executes the action with the given parameters and context.
    /// </summary>
    /// <param name="action">The action definition with type and parameters.</param>
    /// <param name="context">The evaluation context containing current/previous state.</param>
    Task ExecuteAsync(ActionDefinition action, EvaluationContext context);
}

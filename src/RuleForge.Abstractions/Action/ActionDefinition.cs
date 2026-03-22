using RuleForge.Abstractions.Condition;

namespace RuleForge.Abstractions.Action;

/// <summary>
/// Represents an action to execute when a rule condition is met.
/// </summary>
public class ActionDefinition
{
    /// <summary>
    /// The type of action to execute (e.g., "log", "send-email").
    /// Must match an <see cref="IActionExecutor.Type"/> registered in the engine.
    /// </summary>
    public string Type { get; init; } = null!;
    
    /// <summary>
    /// Optional condition that must be met for this action to execute.
    /// Evaluated after the rule's condition passes.
    /// </summary>
    public ConditionNode? Condition { get; init; }
    
    /// <summary>
    /// Parameters for the action executor. The structure depends on the action type.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; init; } = new();
}

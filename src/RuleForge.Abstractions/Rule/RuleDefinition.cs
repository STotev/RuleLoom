using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;

namespace RuleForge.Abstractions.Rule;

/// <summary>
/// Represents a business rule that can be evaluated and executed.
/// Rules are triggered by events and execute actions when their conditions are met.
/// </summary>
public class RuleDefinition
{
    /// <summary>
    /// Unique identifier for this rule. Generated automatically if not provided.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Human-readable name for the rule.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// The event type this rule responds to (e.g., "OrderCreated", "PaymentReceived").
    /// Only rules matching the event type will be evaluated.
    /// </summary>
    public required string EventType { get; init; }
    
    /// <summary>
    /// Execution priority. Higher priority rules execute first.
    /// Rules with the same priority execute in an undefined order.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// The condition that must be met for this rule's actions to execute.
    /// </summary>
    public required ConditionNode Condition { get; init; }
    
    /// <summary>
    /// Actions to execute when the condition is met.
    /// Each action can have its own optional condition.
    /// </summary>
    public List<ActionDefinition> Actions { get; init; } = [];
}

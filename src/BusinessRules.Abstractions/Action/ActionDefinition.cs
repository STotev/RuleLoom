using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Abstractions.Action;

public class ActionDefinition
{
    public string Type { get; init; } = null!;
    public ConditionNode? Condition { get; init; }
    public Dictionary<string, object?> Parameters { get; init; } = new();
}

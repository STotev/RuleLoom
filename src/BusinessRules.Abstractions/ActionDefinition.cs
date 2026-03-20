namespace BusinessRules.Abstractions;

public class ActionDefinition
{
    public string Type { get; init; } = null!;
    public ConditionDefinition? Condition { get; init; }
    public Dictionary<string, object>? Data { get; init; }
}
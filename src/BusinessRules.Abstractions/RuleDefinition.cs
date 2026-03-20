namespace BusinessRules.Abstractions;

public class RuleDefinition
{
    public string Name { get; init; } = null!;
    public string EventName { get; init; } = null!;
    public int Priority { get; init; }

    public ConditionDefinition? Condition { get; init; }
    public List<ActionDefinition> Actions { get; init; } = [];
}
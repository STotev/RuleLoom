using BusinessRules.Abstractions.Action;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Abstractions.Rule;

public class RuleDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = null!;
    public string EventType { get; init; } = null!;
    public int Priority { get; init; }

    public required ConditionNode Condition { get; init; }
    public List<ActionDefinition> Actions { get; init; } = [];
}

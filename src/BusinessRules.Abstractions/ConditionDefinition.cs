namespace BusinessRules.Abstractions;

public class ConditionDefinition
{
    public string Operator { get; init; } = null!;
    public object[] Args { get; init; } = [];
}
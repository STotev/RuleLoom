namespace BusinessRules.Abstractions.Condition;

public class PathOperand : ConditionNode
{
    public required string Path { get; init; }
}

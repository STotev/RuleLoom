namespace BusinessRules.Abstractions.Condition;

public class OperatorCondition : ConditionNode
{
    public required string Operator { get; init; }
    public List<ConditionNode> Operands { get; init; } = [];
}
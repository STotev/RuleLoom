using RuleForge.Abstractions.Condition;

namespace RuleForge.Operators;

public class ChangedOperator : IConditionOperator
{
    public string Name => "changed";
    public int MinOperands => 1;
    public int MaxOperands => 1;

    public bool Evaluate(OperatorContext context)
    {
        var path = context.GetPath(0);

        return !Equals(context.Resolve($"current.{path}"), context.Resolve($"previous.{path}"));
    }
}

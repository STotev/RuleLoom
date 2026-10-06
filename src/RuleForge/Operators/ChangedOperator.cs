using RuleForge.Abstractions.Condition;

namespace RuleForge.Operators;

public class ChangedOperator : IConditionOperator
{
    public string Name => "changed";
    public int MinOperands => 1;
    public int MaxOperands => 1;

    public bool Evaluate(OperatorContext context)
    {
        var property = WithoutScope(context.GetPath(0));

        return !Equals(context.Resolve($"$current.{property}"), context.Resolve($"$previous.{property}"));
    }

    // The operand may already name a state ("$current.Price"); changed always compares both, so keep the property.
    private static string WithoutScope(string path) =>
        path.StartsWith("$current.", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("$previous.", StringComparison.OrdinalIgnoreCase)
            ? path[(path.IndexOf('.') + 1)..]
            : path;
}

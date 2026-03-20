using BusinessRules.Abstractions;

namespace BusinessRules.Core.Operators;

public class ChangedOperator : IConditionOperator
{
    public string Name => "changed";

    public bool Evaluate(EvaluationContext ctx, params object[] args)
    {
        var field = args[0].ToString()!;

        var current = ctx.Resolve(field, useCurrent: true);
        var previous = ctx.Resolve(field, useCurrent: false);

        return !Equals(current, previous);
    }
}
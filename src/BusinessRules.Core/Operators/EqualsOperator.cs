using BusinessRules.Abstractions;

namespace BusinessRules.Core.Operators;

public class EqualsOperator : IConditionOperator
{
    public string Name => "eq";

    public bool Evaluate(EvaluationContext ctx, params object[] args)
    {
        var field = args[0].ToString()!;
        var expected = args[1];

        var value = ctx.Resolve(field);

        return Equals(value, expected);
    }
}
using RuleForge.Abstractions;
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

        return !Equals(
            context.Resolve($"{EvaluationContext.CurrentVariable}.{property}"),
            context.Resolve($"{EvaluationContext.PreviousVariable}.{property}"));
    }

    /// <summary>
    /// <c>changed</c> always compares current with previous, so a <c>$current.</c>/<c>$previous.</c> prefix on its
    /// operand only names the property.
    /// </summary>
    private static string WithoutScope(string path)
    {
        if (!path.StartsWith('$'))
            return path;

        var parts = path.Split('.', 2);
        var isScope = parts[0].Equals(EvaluationContext.CurrentVariable, StringComparison.OrdinalIgnoreCase) ||
                      parts[0].Equals(EvaluationContext.PreviousVariable, StringComparison.OrdinalIgnoreCase);

        if (!isScope)
        {
            throw new InvalidOperationException(
                $"Unknown variable '{parts[0]}' in path '{path}'. Use {EvaluationContext.CurrentVariable} or " +
                $"{EvaluationContext.PreviousVariable}.");
        }

        return parts.Length == 2 ? parts[1] : string.Empty;
    }
}

using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Values;

namespace RuleLoom.Operators;

public class ChangedOperator : IConditionOperator
{
    public string Name => "changed";
    public int MinOperands => 1;
    public int MaxOperands => 1;

    public bool Evaluate(OperatorContext context)
    {
        var text = context.GetPath(0);

        if (!RulePath.TryParse(text, out var path, out var error))
            throw new InvalidOperationException(error);

        if (path.Root == PathRoot.Context)
            throw new InvalidOperationException($"'changed' can't be used on context '{text}': contexts have no previous state.");

        if (path.Property.Length == 0)
            throw new InvalidOperationException($"'changed' needs a property, not the whole state: '{text}'.");

        // The operand may already name a state ("$current.Price"); changed always compares both.
        return !ValueComparer.AreEqual(context.Resolve(path.InState(PathRoot.Current)), context.Resolve(path.InState(PathRoot.Previous)));
    }
}

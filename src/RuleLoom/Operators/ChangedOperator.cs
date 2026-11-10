using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Values;

namespace RuleLoom.Operators;

public class ChangedOperator : IConditionOperator
{
    public string Name => "changed";
    public string Label => "changed";
    public string Description => "True when the path's value differs between the previous and the current state.";
    public int MinOperands => 1;
    public int MaxOperands => 1;

    public bool Evaluate(OperatorContext context)
    {
        var text = context.GetPath(0);

        if (!RulePath.TryParse(text, out var path, out var error))
            throw new InvalidOperationException(error);

        if (Problem(path, text) is { } problem)
            throw new InvalidOperationException(problem);

        // The operand may already name a state ("$current.Price"); changed always compares both.
        return !context.AreEqual(context.Resolve(path.InState(PathRoot.Current)), context.Resolve(path.InState(PathRoot.Previous)));
    }

    public void ValidateOperands(OperandValidationContext context)
    {
        if (context.RequirePath(0) is { } path && Problem(path, ((PathOperand)context.GetOperand(0)).Path) is { } problem)
            context.Error(0, problem);
    }

    // changed compares a property's current and previous value: a context has no previous state, a whole state no property.
    private static string? Problem(RulePath path, string text) =>
        path.Root == PathRoot.Context ? $"'changed' can't be used on a context ('{text}'): contexts have no previous state."
        : path.Property.Length == 0 ? $"'changed' needs a property, not the whole state: '{text}'."
        : null;
}

using RuleLoom.Abstractions.Condition;

namespace RuleLoom.Operators;

/// <summary>
/// The operators that ship with RuleLoom.
/// </summary>
public static class BuiltInOperators
{
    /// <summary>
    /// Creates new instances of every built-in operator: <c>eq</c>, <c>changed</c>, <c>and</c>, <c>or</c>,
    /// <c>not</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.
    /// </summary>
    public static IReadOnlyList<IConditionOperator> Create() =>
    [
        new EqualsOperator(),
        new ChangedOperator(),
        new AndOperator(),
        new OrOperator(),
        new NotOperator(),
        new GreaterThanOperator(),
        new GreaterThanOrEqualOperator(),
        new LessThanOperator(),
        new LessThanOrEqualOperator()
    ];
}

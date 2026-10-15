using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Evaluators;
using RuleLoom.Operators;

namespace RuleLoom.Tests;

/// <summary>
/// Builders for condition trees and an evaluator with the built-in operators.
/// </summary>
internal static class Conditions
{
    public static OperatorCondition Op(string name, params ConditionNode[] operands) =>
        new() { Operator = name, Operands = [.. operands] };

    public static PathOperand Path(string path) => new() { Path = path };

    public static ValueOperand Value(object? value) => new() { Value = value };

    public static ConditionEvaluator Evaluator(params IConditionOperator[] extraOperators) =>
        new([
            new EqualsOperator(),
            new ChangedOperator(),
            new AndOperator(),
            new OrOperator(),
            new NotOperator(),
            new GreaterThanOperator(),
            new GreaterThanOrEqualOperator(),
            new LessThanOperator(),
            new LessThanOrEqualOperator(),
            .. extraOperators
        ]);

    /// <summary>
    /// Creates one <see cref="CountingOperator"/> per result, operator nodes referencing them,
    /// and an evaluator that has them registered.
    /// </summary>
    public static (ConditionEvaluator Evaluator, ConditionNode[] Operands, CountingOperator[] Counters)
        CountingOperands(params bool[] results)
    {
        var counters = results.Select((r, i) => new CountingOperator($"op{i}", r)).ToArray();
        var operands = counters.Select(c => (ConditionNode)Op(c.Name)).ToArray();

        return (Evaluator(counters), operands, counters);
    }
}

/// <summary>
/// Operator that returns a fixed result and counts how often it was evaluated.
/// </summary>
internal sealed class CountingOperator(string name, bool result) : IConditionOperator
{
    public int Calls { get; private set; }

    public string Name => name;
    public int MinOperands => 0;
    public int MaxOperands => 0;

    public bool Evaluate(OperatorContext context)
    {
        Calls++;
        return result;
    }
}

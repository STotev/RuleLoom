using RuleForge.Abstractions;
using RuleForge.Abstractions.Condition;
using RuleForge.Operators;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

public class OrderingOperatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Rows of (left, right, ordering): -1 = left is less, 0 = equal, 1 = greater, null = can't be ordered.
    /// </summary>
    public static TheoryData<object?, object?, int?> Pairs => new()
    {
        // Numbers across types
        { 150m, 100, 1 },
        { 100m, 100, 0 },
        { 99.5, 100L, -1 },
        { (byte)3, 2.5f, 1 },
        // Dates, including ISO 8601 strings
        { Noon, "2026-05-01T11:59:59Z", 1 },
        { Noon.UtcDateTime, "2026-05-01T14:00:00+02:00", 0 },
        { new DateTime(2026, 4, 30, 23, 0, 0, DateTimeKind.Utc), "2026-05-01", -1 },
        // Enums by name or number
        { Status.Inactive, "Active", 1 },
        { Status.Active, 1, 0 },
        { Status.Draft, "active", -1 },
        // Strings, ordinal
        { "b", "a", 1 },
        { "a", "a", 0 },
        { "B", "a", -1 },
        // Can't be ordered
        { null, 5, null },
        { 5, null, null },
        { null, null, null },
        { 100m, "abc", null },
        { 100, "100", null },
        { double.NaN, 1, null },
        { Noon, "05/01/2026", null },
        { Status.Active, OtherStatus.Active, null },
        { new object(), new object(), null }
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Evaluate_Pair_FollowsOrdering(object? left, object? right, int? ordering)
    {
        Assert.Equal(ordering > 0, Evaluate(new GreaterThanOperator(), left, right));
        Assert.Equal(ordering >= 0, Evaluate(new GreaterThanOrEqualOperator(), left, right));
        Assert.Equal(ordering < 0, Evaluate(new LessThanOperator(), left, right));
        Assert.Equal(ordering <= 0, Evaluate(new LessThanOrEqualOperator(), left, right));
    }

    [Fact]
    public void Evaluate_PathOperands_CompareResolvedValues()
    {
        var context = new EvaluationContext(new { Price = 150m, Budget = 200 }, new { Price = 120m, Budget = 200 });

        Assert.True(Evaluator().Evaluate(context, Op("lt", Path("Price"), Path("Budget"))));
        Assert.True(Evaluator().Evaluate(context, Op("gt", Path("Price"), Path("previous.Price"))));
    }

    [Fact]
    public void Evaluate_MissingProperty_IsFalseForAllOperators()
    {
        var context = new EvaluationContext(new { Price = 150m });

        foreach (var name in new[] { "gt", "gte", "lt", "lte" })
            Assert.False(Evaluator().Evaluate(context, Op(name, Path("Missing"), Value(1))));
    }

    private static bool Evaluate(IConditionOperator op, object? left, object? right) =>
        op.Evaluate(new OperatorContext(Op(op.Name, Value(left), Value(right)), new EvaluationContext(new { })));

    public enum Status
    {
        Draft,
        Active,
        Inactive
    }

    public enum OtherStatus
    {
        Active
    }
}

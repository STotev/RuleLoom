using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Evaluators;
using RuleLoom.Operators;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class ConditionEvaluatorTests
{
    private static readonly EvaluationContext Context = new(new { Price = 100, Name = "Lamp" });

    [Theory]
    [InlineData("eq", 2, 2)]
    [InlineData("changed", 1, 1)]
    [InlineData("and", 1, int.MaxValue)]
    [InlineData("or", 1, int.MaxValue)]
    [InlineData("not", 1, 1)]
    [InlineData("gt", 2, 2)]
    [InlineData("gte", 2, 2)]
    [InlineData("lt", 2, 2)]
    [InlineData("lte", 2, 2)]
    public void BuiltInOperators_DeclareNameAndOperandLimits(string name, int min, int max)
    {
        IConditionOperator[] operators =
        [
            new EqualsOperator(), new ChangedOperator(), new AndOperator(), new OrOperator(), new NotOperator(),
            new GreaterThanOperator(), new GreaterThanOrEqualOperator(), new LessThanOperator(), new LessThanOrEqualOperator()
        ];

        var op = Assert.Single(operators, o => o.Name == name);

        Assert.Equal(min, op.MinOperands);
        Assert.Equal(max, op.MaxOperands);
    }

    [Fact]
    public void Constructor_NullOperators_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ConditionEvaluator(null!));
    }

    [Theory]
    [InlineData("eq")]
    [InlineData("EQ")]
    [InlineData("Eq")]
    public void Evaluate_OperatorNameInAnyCase_IsFound(string name)
    {
        Assert.True(Evaluator().Evaluate(Context, Op(name, Path("Price"), Value(100))));
    }

    [Fact]
    public void Evaluate_RegisteredCustomOperator_IsUsed()
    {
        var custom = new CountingOperator("custom", true);

        Assert.True(Evaluator(custom).Evaluate(Context, Op("custom")));
        Assert.Equal(1, custom.Calls);
    }

    [Fact]
    public void Evaluate_UnknownOperator_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Evaluator().Evaluate(Context, Op("no-such-operator", Value(1), Value(2))));
    }

    [Fact]
    public void Evaluate_TooFewOperands_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Evaluator().Evaluate(Context, Op("eq", Value(1))));
    }

    [Fact]
    public void Evaluate_TooManyOperands_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Evaluator().Evaluate(Context, Op("eq", Value(1), Value(1), Value(1))));
    }

    [Fact]
    public void Evaluate_OperandCountOutOfRange_DoesNotCallOperator()
    {
        var limited = new CountingOperator("limited", true);

        Assert.Throws<InvalidOperationException>(() => Evaluator(limited).Evaluate(Context, Op("limited", Value(1))));
        Assert.Equal(0, limited.Calls);
    }

    [Theory]
    [MemberData(nameof(NonOperatorNodes))]
    public void Evaluate_NonOperatorNode_ThrowsNamingTheNodeType(ConditionNode node)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Evaluator().Evaluate(Context, node));

        Assert.Equal($"Expected an operator condition but got {node.GetType().Name}.", ex.Message);
        Assert.DoesNotContain("root", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<ConditionNode> NonOperatorNodes => new() { Path("Price"), Value(true) };
}

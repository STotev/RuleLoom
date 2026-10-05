using RuleForge.Abstractions;
using RuleForge.Abstractions.Condition;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

public class OperatorContextTests
{
    private static readonly EvaluationContext State = new(
        new { Name = "Lamp", Price = 100m, IsActive = true, Tags = (string?)null },
        new { Name = "Lamp", Price = 90m, IsActive = false, Tags = (string?)null });

    [Fact]
    public void Properties_DescribeTheCondition()
    {
        var context = new OperatorContext(Op("custom", Value(1), Path("Price")), State);

        Assert.Equal("custom", context.OperatorName);
        Assert.Equal(2, context.OperandCount);
        Assert.Same(State, context.EvaluationContext);
    }

    [Fact]
    public void GetValue_ValueOperand_ReturnsValue()
    {
        Assert.Equal(42, Context(Value(42)).GetValue(0));
    }

    [Fact]
    public void GetValue_PathOperand_ReturnsResolvedValue()
    {
        Assert.Equal(100m, Context(Path("Price")).GetValue(0));
        Assert.Equal(90m, Context(Path("previous.Price")).GetValue(0));
        Assert.Null(Context(Path("Missing")).GetValue(0));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(1, false)]
    public void GetValue_NestedOperator_ReturnsItsBooleanResult(int value, bool expected)
    {
        var context = new OperatorContext(Op("custom", Op("eq", Path("Price"), Value(value))), State, Evaluator());

        Assert.Equal(expected, context.GetValue(0));
    }

    [Fact]
    public void Test_NestedOperator_IsEvaluatedOnlyWhenCalled()
    {
        var counting = new CountingOperator("counted", true);
        var context = new OperatorContext(Op("custom", Op("counted"), Op("counted")), State, Evaluator(counting));

        Assert.True(context.Test(1));
        Assert.Equal(1, counting.Calls);
    }

    [Theory]
    [InlineData("IsActive", true)]
    [InlineData("previous.IsActive", false)]
    public void Test_PathResolvingToBool_ReturnsIt(string path, bool expected)
    {
        Assert.Equal(expected, Context(Path(path)).Test(0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_BoolValue_ReturnsIt(bool value)
    {
        Assert.Equal(value, Context(Value(value)).Test(0));
    }

    [Fact]
    public void Test_PathNotResolvingToBool_ThrowsDescriptiveError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Context(Path("Name")).Test(0));

        Assert.Equal(
            "Operator 'custom' expects a condition at operand 0, but path 'Name' resolved to String instead of a boolean.",
            ex.Message);
    }

    [Fact]
    public void Test_NonBoolValue_ThrowsDescriptiveError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Context(Value(null)).Test(0));

        Assert.Equal(
            "Operator 'custom' expects a condition at operand 0, but the value is null instead of a boolean.",
            ex.Message);
    }

    [Fact]
    public void Test_NestedOperatorWithoutEvaluator_ThrowsDescriptiveError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Context(Op("eq", Value(1), Value(1))).Test(0));

        Assert.Contains("needs an IConditionEvaluator", ex.Message);
        Assert.Contains("'eq'", ex.Message);
    }

    [Fact]
    public void GetPath_PathOperand_ReturnsPathString()
    {
        Assert.Equal("Address.City", Context(Path("Address.City")).GetPath(0));
    }

    [Fact]
    public void GetPath_NonPathOperand_ThrowsDescriptiveError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Context(Value("Price")).GetPath(0));

        Assert.Equal("Operator 'custom' expects a path at operand 0, but got ValueOperand.", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Accessors_IndexOutOfRange_Throw(int index)
    {
        var context = Context(Value(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => context.GetValue(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.Test(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.GetPath(index));
    }

    [Fact]
    public void Resolve_AnyPath_ResolvesAgainstState()
    {
        var context = Context();

        Assert.Equal(90m, context.Resolve("previous.Price"));
        Assert.Equal("Lamp", context.Resolve("Name"));
    }

    [Fact]
    public void Comparisons_UseSharedComparisonRules()
    {
        var context = Context();

        Assert.True(context.AreEqual(100m, 100));
        Assert.True(context.TryCompare(1, 2.5, out var result));
        Assert.Equal(-1, result);
        Assert.False(context.TryCompare(1, "1", out _));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new OperatorContext(null!, State));
        Assert.Throws<ArgumentNullException>(() => new OperatorContext(Op("custom"), null!));
    }

    private static OperatorContext Context(params ConditionNode[] operands) => new(Op("custom", operands), State);
}

/// <summary>
/// The plugin-author path from the spec: a custom operator, unit-tested through a hand-built OperatorContext.
/// </summary>
public class CustomOperatorTests
{
    [Theory]
    [InlineData("Summer sale", "sale", true)]
    [InlineData("Summer sale", "Sale", false)]
    [InlineData("Summer", "sale", false)]
    public void Evaluate_TextAndPart_ChecksContainment(string title, string part, bool expected)
    {
        var condition = new OperatorCondition
        {
            Operator = "contains",
            Operands = [new PathOperand { Path = "Title" }, new ValueOperand { Value = part }]
        };
        var context = new OperatorContext(condition, new EvaluationContext(new { Title = title }));

        Assert.Equal(expected, new ContainsOperator().Evaluate(context));
    }

    [Fact]
    public void Evaluate_RegisteredWithEvaluator_WorksInRules()
    {
        var evaluator = Evaluator(new ContainsOperator());
        var condition = Op("and", Op("contains", Path("Title"), Value("sale")), Op("eq", Path("Stock"), Value(5)));

        Assert.True(evaluator.Evaluate(new EvaluationContext(new { Title = "Summer sale", Stock = 5 }), condition));
    }

    private sealed class ContainsOperator : IConditionOperator
    {
        public string Name => "contains";
        public int MinOperands => 2;
        public int MaxOperands => 2;

        public bool Evaluate(OperatorContext ctx) =>
            ctx.GetValue(0) is string text && ctx.GetValue(1) is string part &&
            text.Contains(part, StringComparison.Ordinal);
    }
}

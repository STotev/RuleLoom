using RuleForge.Abstractions;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

public class EqualsOperatorTests
{
    private static readonly Product Item = new() { Name = "Lamp", Price = 100, EstimatedPrice = 100, Discount = null };

    [Theory]
    [InlineData(100, true)]
    [InlineData(99, false)]
    public void Evaluate_PathAgainstValue_ComparesResolvedValue(int value, bool expected)
    {
        var result = Evaluator().Evaluate(new EvaluationContext(Item), Op("eq", Path("Price"), Value(value)));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_ValueOnEitherSide_GivesSameResult()
    {
        var context = new EvaluationContext(Item);

        Assert.True(Evaluator().Evaluate(context, Op("eq", Value(100), Path("Price"))));
    }

    [Fact]
    public void Evaluate_TwoPaths_ComparesBothResolvedValues()
    {
        var result = Evaluator().Evaluate(new EvaluationContext(Item), Op("eq", Path("Price"), Path("EstimatedPrice")));

        Assert.True(result);
    }

    [Theory]
    [InlineData("Lamp", true)]
    [InlineData("lamp", false)]
    [InlineData("Lamp ", false)]
    public void Evaluate_Strings_ComparesOrdinally(string value, bool expected)
    {
        var result = Evaluator().Evaluate(new EvaluationContext(Item), Op("eq", Path("Name"), Value(value)));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_NullPropertyAgainstNullValue_ReturnsTrue()
    {
        var result = Evaluator().Evaluate(new EvaluationContext(Item), Op("eq", Path("Discount"), Value(null)));

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_NullPropertyAgainstValue_ReturnsFalse()
    {
        var result = Evaluator().Evaluate(new EvaluationContext(Item), Op("eq", Path("Discount"), Value(5)));

        Assert.False(result);
    }

    [Fact]
    public void Evaluate_MissingPropertyAgainstNullValue_ReturnsTrue()
    {
        var result = Evaluator().Evaluate(new EvaluationContext(Item), Op("eq", Path("Missing"), Value(null)));

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_NestedOperatorOperand_ComparesItsBooleanResult()
    {
        var context = new EvaluationContext(Item, new Product { Name = "Lamp", Price = 90 });

        var result = Evaluator().Evaluate(context, Op("eq", Op("changed", Path("Price")), Value(true)));

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_PreviousScope_ComparesPreviousState()
    {
        var context = new EvaluationContext(Item, new Product { Name = "Desk", Price = 90 });

        Assert.True(Evaluator().Evaluate(context, Op("eq", Path("previous.Name"), Value("Desk"))));
    }

    private sealed class Product
    {
        public string Name { get; init; } = string.Empty;
        public int Price { get; init; }
        public int EstimatedPrice { get; init; }
        public int? Discount { get; init; }
    }
}

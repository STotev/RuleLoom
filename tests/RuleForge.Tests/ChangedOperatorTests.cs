using RuleForge.Abstractions;
using RuleForge.Operators;
using RuleForge.Abstractions.Condition;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

public class ChangedOperatorTests
{
    [Theory]
    [InlineData(100, 90, true)]
    [InlineData(100, 100, false)]
    public void Evaluate_ValueTypeProperty_DetectsChange(int current, int previous, bool expected)
    {
        var context = new EvaluationContext(new Order { Price = current }, new Order { Price = previous });

        Assert.Equal(expected, Evaluator().Evaluate(context, Op("changed", Path("Price"))));
    }

    [Theory]
    [InlineData("Active", "Draft", true)]
    [InlineData("Active", "Active", false)]
    public void Evaluate_ReferenceTypeProperty_ComparesByValue(string current, string previous, bool expected)
    {
        var context = new EvaluationContext(
            new Order { Status = current },
            new Order { Status = new string(previous.ToCharArray()) });

        Assert.Equal(expected, Evaluator().Evaluate(context, Op("changed", Path("Status"))));
    }

    [Theory]
    [InlineData("Paris", "Rome", true)]
    [InlineData("Paris", "Paris", false)]
    public void Evaluate_NestedPath_DetectsChange(string current, string previous, bool expected)
    {
        var context = new EvaluationContext(
            new Order { Address = new Address { City = current } },
            new Order { Address = new Address { City = previous } });

        Assert.Equal(expected, Evaluator().Evaluate(context, Op("changed", Path("Address.City"))));
    }

    [Fact]
    public void Evaluate_BothValuesNull_ReturnsFalse()
    {
        var context = new EvaluationContext(new Order { Status = null }, new Order { Status = null });

        Assert.False(Evaluator().Evaluate(context, Op("changed", Path("Status"))));
    }

    [Fact]
    public void Evaluate_ValueSetFromNull_ReturnsTrue()
    {
        var context = new EvaluationContext(new Order { Status = "Active" }, new Order { Status = null });

        Assert.True(Evaluator().Evaluate(context, Op("changed", Path("Status"))));
    }

    [Fact]
    public void Evaluate_NoPreviousState_TreatsPreviousValueAsNull()
    {
        var context = new EvaluationContext(new Order { Status = "Active" });

        Assert.True(Evaluator().Evaluate(context, Op("changed", Path("Status"))));
    }

    [Theory]
    [InlineData("Price")]
    [InlineData("$current.Price")]
    [InlineData("$previous.Price")]
    [InlineData("$CURRENT.Price")]
    public void Evaluate_ScopedOrUnscopedPath_ComparesCurrentWithPrevious(string path)
    {
        var changed = new EvaluationContext(new Order { Price = 100 }, new Order { Price = 90 });
        var unchanged = new EvaluationContext(new Order { Price = 100 }, new Order { Price = 100 });

        Assert.True(Evaluator().Evaluate(changed, Op("changed", Path(path))));
        Assert.False(Evaluator().Evaluate(unchanged, Op("changed", Path(path))));
    }

    [Fact]
    public void Evaluate_NonPathOperand_Throws()
    {
        var context = new EvaluationContext(new Order(), new Order());

        Assert.Throws<InvalidOperationException>(() => Evaluator().Evaluate(context, Op("changed", Value("Price"))));
    }

    [Theory]
    [InlineData(100, 90, true)]
    [InlineData(100, 100, false)]
    public void Evaluate_DirectlyWithOperatorContext_DetectsChange(int current, int previous, bool expected)
    {
        var context = new OperatorContext(
            Op("changed", Path("Price")),
            new EvaluationContext(new Order { Price = current }, new Order { Price = previous }));

        Assert.Equal(expected, new ChangedOperator().Evaluate(context));
    }

    private sealed class Order
    {
        public int Price { get; init; }
        public string? Status { get; init; }
        public Address Address { get; init; } = new();
    }

    private sealed class Address
    {
        public string City { get; init; } = string.Empty;
    }
}

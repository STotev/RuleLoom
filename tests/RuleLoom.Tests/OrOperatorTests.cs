using RuleLoom.Abstractions;
using RuleLoom.Operators;
using RuleLoom.Abstractions.Condition;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class OrOperatorTests
{
    private static readonly EvaluationContext Context = new(new { });

    [Theory]
    [InlineData(new[] { true }, true)]
    [InlineData(new[] { false }, false)]
    [InlineData(new[] { false, false, false }, false)]
    [InlineData(new[] { false, true, false }, true)]
    public void Evaluate_Operands_ReturnsTrueWhenAnyIsTrue(bool[] operandResults, bool expected)
    {
        var (evaluator, operands, _) = CountingOperands(operandResults);

        Assert.Equal(expected, evaluator.Evaluate(Context, Op("or", operands)));
    }

    [Fact]
    public void Evaluate_TrueOperand_SkipsRemainingOperands()
    {
        var (evaluator, operands, counters) = CountingOperands(false, true, false);

        evaluator.Evaluate(Context, Op("or", operands));

        Assert.Equal([1, 1, 0], counters.Select(c => c.Calls));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Evaluate_DirectlyWithBoolOperands_CombinesThem(bool first, bool second, bool expected)
    {
        var context = new OperatorContext(Op("or", Value(first), Value(second)), Context);

        Assert.Equal(expected, new OrOperator().Evaluate(context));
    }

    [Fact]
    public void Evaluate_BoolPathOperands_AreAccepted()
    {
        var context = new EvaluationContext(new { IsActive = true, IsDeleted = false });

        Assert.True(Evaluator().Evaluate(context, Op("or", Path("IsActive"), Path("IsDeleted"))));
    }
}

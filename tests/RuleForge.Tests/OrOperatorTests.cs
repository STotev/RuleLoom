using RuleForge.Abstractions;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

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
}

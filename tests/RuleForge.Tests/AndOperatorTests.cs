using RuleForge.Abstractions;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

public class AndOperatorTests
{
    private static readonly EvaluationContext Context = new(new { });

    [Theory]
    [InlineData(new[] { true }, true)]
    [InlineData(new[] { false }, false)]
    [InlineData(new[] { true, true, true }, true)]
    [InlineData(new[] { true, false, true }, false)]
    public void Evaluate_Operands_ReturnsTrueOnlyWhenAllAreTrue(bool[] operandResults, bool expected)
    {
        var (evaluator, operands, _) = CountingOperands(operandResults);

        Assert.Equal(expected, evaluator.Evaluate(Context, Op("and", operands)));
    }

    [Fact]
    public void Evaluate_FalseOperand_SkipsRemainingOperands()
    {
        var (evaluator, operands, counters) = CountingOperands(true, false, true);

        evaluator.Evaluate(Context, Op("and", operands));

        Assert.Equal([1, 1, 0], counters.Select(c => c.Calls));
    }

    [Fact]
    public void Evaluate_NestedLogicalOperators_EvaluatesWholeTree()
    {
        var context = new EvaluationContext(new { Status = "Active", Price = 100 });
        var condition = Op("and",
            Op("eq", Path("Status"), Value("Active")),
            Op("not", Op("or",
                Op("eq", Path("Price"), Value(0)),
                Op("eq", Path("Price"), Value(1)))));

        Assert.True(Evaluator().Evaluate(context, condition));
    }
}

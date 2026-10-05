using RuleForge.Abstractions;
using RuleForge.Operators;
using RuleForge.Abstractions.Condition;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

public class NotOperatorTests
{
    private static readonly EvaluationContext Context = new(new { });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Evaluate_Operand_InvertsResult(bool operandResult, bool expected)
    {
        var (evaluator, operands, _) = CountingOperands(operandResult);

        Assert.Equal(expected, evaluator.Evaluate(Context, Op("not", operands)));
    }

    [Fact]
    public void Evaluate_NestedNot_CancelsOut()
    {
        var context = new EvaluationContext(new { Price = 100 });

        Assert.True(Evaluator().Evaluate(context, Op("not", Op("not", Op("eq", Path("Price"), Value(100))))));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Evaluate_DirectlyWithBoolOperand_InvertsIt(bool operand, bool expected)
    {
        Assert.Equal(expected, new NotOperator().Evaluate(new OperatorContext(Op("not", Value(operand)), Context)));
    }
}

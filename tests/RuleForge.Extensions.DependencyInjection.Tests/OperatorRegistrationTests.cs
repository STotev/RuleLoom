using Microsoft.Extensions.DependencyInjection;
using RuleForge.Abstractions;
using RuleForge.Abstractions.Condition;
using RuleForge.Operators;

namespace RuleForge.Extensions.DependencyInjection.Tests;

public class OperatorRegistrationTests
{
    private static readonly EvaluationContext Context = new(new { Title = "Summer sale", Price = 100 });

    [Theory]
    [InlineData("eq")]
    [InlineData("changed")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("not")]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    public void AddRuleForge_RegistersBuiltInOperators(string name)
    {
        var evaluator = Evaluator(new ServiceCollection().AddRuleForge());

        var ex = Record.Exception(() => evaluator.Evaluate(Context, Op(name)));

        Assert.DoesNotContain("not found", ex?.Message ?? string.Empty);
    }

    [Fact]
    public void AddOperator_CustomOperator_IsAvailable()
    {
        var evaluator = Evaluator(new ServiceCollection().AddRuleForge(rules => rules.AddOperator<ContainsOperator>()));

        Assert.True(evaluator.Evaluate(Context, Op("contains", Path("Title"), Value("sale"))));
    }

    [Fact]
    public void AddOperator_NameClashWithBuiltIn_FailsWhenEvaluatorIsBuilt()
    {
        var services = new ServiceCollection().AddRuleForge(rules => rules.AddOperator<AlwaysTrueEqualsOperator>());

        var ex = Assert.Throws<ArgumentException>(() => Evaluator(services));

        Assert.StartsWith("Operator 'eq' is registered more than once.", ex.Message);
    }

    [Fact]
    public void ReplaceOperator_SwapsTheBuiltInWithTheSameName()
    {
        var evaluator = Evaluator(new ServiceCollection().AddRuleForge(rules => rules.ReplaceOperator<AlwaysTrueEqualsOperator>()));

        Assert.True(evaluator.Evaluate(Context, Op("eq", Value(1), Value(2))));
    }

    [Fact]
    public void RemoveOperator_RemovesBuiltIn()
    {
        var evaluator = Evaluator(new ServiceCollection().AddRuleForge(rules => rules.RemoveOperator("CHANGED")));

        var ex = Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate(Context, Op("changed", Path("Price"))));
        Assert.Contains("'changed' not found", ex.Message);
    }

    [Fact]
    public void RemoveThenAdd_AppliesInCallOrder()
    {
        var evaluator = Evaluator(new ServiceCollection().AddRuleForge(rules => rules
            .RemoveOperator("eq")
            .AddOperator<AlwaysTrueEqualsOperator>()));

        Assert.True(evaluator.Evaluate(Context, Op("eq", Value(1), Value(2))));
    }

    [Fact]
    public void Evaluator_IsSingleton()
    {
        using var provider = new ServiceCollection().AddRuleForge().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IConditionEvaluator>(), provider.GetRequiredService<IConditionEvaluator>());
    }

    [Fact]
    public void BuiltInOperators_Create_ReturnsNewInstancesOfAllNine()
    {
        var first = BuiltInOperators.Create();
        var second = BuiltInOperators.Create();

        Assert.Equal(["and", "changed", "eq", "gt", "gte", "lt", "lte", "not", "or"], first.Select(o => o.Name).Order());
        Assert.All(first.Zip(second), pair => Assert.NotSame(pair.First, pair.Second));
    }

    private static IConditionEvaluator Evaluator(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IConditionEvaluator>();

    private static OperatorCondition Op(string name, params ConditionNode[] operands) => new() { Operator = name, Operands = [.. operands] };
    private static PathOperand Path(string path) => new() { Path = path };
    private static ValueOperand Value(object? value) => new() { Value = value };

    private sealed class ContainsOperator : IConditionOperator
    {
        public string Name => "contains";
        public int MinOperands => 2;
        public int MaxOperands => 2;

        public bool Evaluate(OperatorContext ctx) =>
            ctx.GetValue(0) is string text && ctx.GetValue(1) is string part && text.Contains(part, StringComparison.Ordinal);
    }

    private sealed class AlwaysTrueEqualsOperator : IConditionOperator
    {
        public string Name => "eq";
        public int MinOperands => 2;
        public int MaxOperands => 2;
        public bool Evaluate(OperatorContext ctx) => true;
    }
}

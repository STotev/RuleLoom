using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Abstractions;
using RuleLoom.Operators;
using RuleLoom.Abstractions.Condition;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

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

    [Fact]
    public void Evaluate_SameValueOfDifferentTypeOrFormat_ReturnsFalse()
    {
        var number = new EvaluationContext(new { Total = 100L }, new { Total = 100.0m });
        var instant = new EvaluationContext(new { DueAt = "2026-05-01T14:00:00+02:00" }, new { DueAt = "2026-05-01T12:00:00Z" });

        Assert.False(Evaluator().Evaluate(number, Op("changed", Path("Total"))));
        Assert.False(Evaluator().Evaluate(instant, Op("changed", Path("DueAt"))));
    }

    [Theory]
    [InlineData("""{ "Customer": { "Name": "Ada", "Tier": "VIP" } }""", false)]
    [InlineData("""{ "Customer": { "Tier": "VIP", "Name": "Ada" } }""", false)]
    [InlineData("""{ "Customer": { "Name": "Ada", "Tier": "Basic" } }""", true)]
    public void Evaluate_JsonObjectPath_ComparesContent(string current, bool expected)
    {
        var previous = """{ "Customer": { "Name": "Ada", "Tier": "VIP" } }""";
        var elements = new EvaluationContext(JsonDocument.Parse(current).RootElement, JsonDocument.Parse(previous).RootElement);
        var nodes = new EvaluationContext(JsonNode.Parse(current), JsonNode.Parse(previous));

        Assert.Equal(expected, Evaluator().Evaluate(elements, Op("changed", Path("Customer"))));
        Assert.Equal(expected, Evaluator().Evaluate(nodes, Op("changed", Path("Customer"))));
    }

    [Theory]
    [InlineData("""{ "Status": "AwaitingPayment" }""", "Status", true)]
    [InlineData("""{ "Status": "AwaitingPayment" }""", "Total", false)]
    [InlineData("""{ "Status": null }""", "Status", true)]
    [InlineData("""{ "Customer": { "Email": "old@example.com" } }""", "Customer.Email", true)]
    [InlineData("""{ "Customer": { "Email": "old@example.com" } }""", "Customer.Name", false)]
    [InlineData("""{ "Customer": { "Email": "old@example.com" } }""", "Customer", true)]
    public void Evaluate_PreviousStateFromChanges_DetectsChange(string changes, string path, bool expected)
    {
        var current = JsonNode.Parse("""{ "Status": "Paid", "Total": 640.5, "Customer": { "Name": "Ada", "Email": "ada@example.com" } }""")!;
        var context = new EvaluationContext(current, PreviousState.FromChanges(current, JsonNode.Parse(changes)!));

        Assert.Equal(expected, Evaluator().Evaluate(context, Op("changed", Path(path))));
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

    [Theory]
    [InlineData("$current", "needs a property")]
    [InlineData("$previous.@customer", "Contexts have no current or previous state")]
    [InlineData("#now", "reserved for functions")]
    public void Evaluate_WholeStateOrInvalidPath_ThrowsInvalidOperation(string path, string message)
    {
        var context = new EvaluationContext(new Order { Price = 1 }, new Order { Price = 2 });

        var error = Assert.Throws<InvalidOperationException>(() => Evaluator().Evaluate(context, Op("changed", Path(path))));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("Price", null)]
    [InlineData("$previous.Price", null)]
    [InlineData("@customer.Tier", "'changed' can't be used on a context ('@customer.Tier'): contexts have no previous state.")]
    [InlineData("$current", "'changed' needs a property, not the whole state: '$current'.")]
    public void ValidateOperands_ReportsWhatEvaluateRejects(string path, string? message)
    {
        var context = new OperandValidationContext(Op("changed", Path(path)));

        new ChangedOperator().ValidateOperands(context);

        Assert.Equal(message is null ? [] : [new OperandProblem(0, message, IsWarning: false)], context.Problems);
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

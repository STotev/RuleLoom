using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Abstractions;

namespace RuleLoom.Tests;

public class PreviousStateTests
{
    private const string OrderJson = """
    { "Status": "Paid", "Total": 640.5, "Id": "8b0e9c1a-0d5e-4d55-9a57-2f6c1c3e8a11", "Customer": { "Name": "Ada", "Email": "ada@example.com" } }
    """;

    private static readonly Order OrderObject = new()
    {
        Status = "Paid",
        Total = 640.5m,
        Id = Guid.Parse("8b0e9c1a-0d5e-4d55-9a57-2f6c1c3e8a11"),
        Customer = new Customer { Name = "Ada", Email = "ada@example.com" }
    };

    public static TheoryData<string> CurrentKinds() => ["element", "node", "object"];

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_ChangedField_ReturnsOldValue(string kind)
    {
        var context = Context(kind, """{ "Status": "AwaitingPayment" }""");

        Assert.Equal("AwaitingPayment", context.Resolve("$previous.Status"));
        Assert.Equal("Paid", context.Resolve("$current.Status"));
    }

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_UnchangedField_ReturnsCurrentValue(string kind)
    {
        var context = Context(kind, """{ "Status": "AwaitingPayment" }""");

        Assert.Equal(640.5m, context.Resolve("$previous.Total"));
        Assert.Equal("ada@example.com", context.Resolve("$previous.Customer.Email"));
    }

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_NestedChange_ReturnsOldValueAndKeepsSiblingsFromCurrent(string kind)
    {
        var context = Context(kind, """{ "Customer": { "Email": "old@example.com" } }""");

        Assert.Equal("old@example.com", context.Resolve("$previous.Customer.Email"));
        Assert.Equal("Ada", context.Resolve("$previous.Customer.Name"));
    }

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_FieldThatWasNull_ReturnsNull(string kind)
    {
        var context = Context(kind, """{ "Status": null }""");

        Assert.Null(context.Resolve("$previous.Status"));
    }

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_ParentThatWasNull_ReturnsNullBelowIt(string kind)
    {
        var context = Context(kind, """{ "Customer": null }""");

        Assert.Null(context.Resolve("$previous.Customer.Email"));
        Assert.Null(context.Resolve("$previous.Customer"));
    }

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_KeyOnlyInChanges_ReturnsOldValue(string kind)
    {
        var context = Context(kind, """{ "Discount": 10 }""");

        Assert.Equal(10L, context.Resolve("$previous.Discount"));
        Assert.Null(context.Resolve("$current.Discount"));
    }

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_ChangedKeyInAnyCase_ReturnsOldValue(string kind)
    {
        var context = Context(kind, """{ "status": "AwaitingPayment" }""");

        Assert.Equal("AwaitingPayment", context.Resolve("$previous.STATUS"));
    }

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_AmbiguousChangedKey_FallsBackToCurrent(string kind)
    {
        var context = Context(kind, """{ "status": "A", "STATUS": "B" }""");

        Assert.Equal("Paid", context.Resolve("$previous.Status"));
    }

    [Theory]
    [MemberData(nameof(CurrentKinds))]
    public void Resolve_PathEndingAtChangedObject_ReturnsTheChanges(string kind)
    {
        var context = Context(kind, """{ "Customer": { "Email": "old@example.com" } }""");

        var customer = context.Resolve("$previous.Customer");

        Assert.True(customer is JsonElement or JsonNode);
    }

    [Fact]
    public void Resolve_PreviousAlone_ReturnsTheState()
    {
        var previous = PreviousState.FromChanges(OrderObject, JsonNode.Parse("""{ "Status": "AwaitingPayment" }""")!);

        Assert.Same(previous, new EvaluationContext(OrderObject, previous).Resolve("$previous"));
    }

    [Fact]
    public void Resolve_UnchangedValueOfClrType_KeepsItsType()
    {
        var context = Context("object", """{ "Status": "AwaitingPayment" }""");

        Assert.Equal(OrderObject.Id, context.Resolve("$previous.Id"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public void FromChanges_NonObjectJson_Throws(string changes)
    {
        Assert.Throws<ArgumentException>(() => PreviousState.FromChanges(OrderObject, JsonDocument.Parse(changes).RootElement));
    }

    [Fact]
    public void FromChanges_NonJsonChanges_Throws()
    {
        Assert.Throws<ArgumentException>(() => PreviousState.FromChanges(OrderObject, new { Status = "AwaitingPayment" }));
    }

    private static EvaluationContext Context(string kind, string changesJson)
    {
        object current = kind switch
        {
            "element" => JsonDocument.Parse(OrderJson).RootElement,
            "node" => JsonNode.Parse(OrderJson)!,
            _ => OrderObject
        };

        object changes = kind == "node" ? JsonNode.Parse(changesJson)! : JsonDocument.Parse(changesJson).RootElement;

        return new EvaluationContext(current, PreviousState.FromChanges(current, changes));
    }

    private sealed class Order
    {
        public string? Status { get; init; }
        public decimal Total { get; init; }
        public Guid Id { get; init; }
        public Customer? Customer { get; init; }
    }

    private sealed class Customer
    {
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
    }
}

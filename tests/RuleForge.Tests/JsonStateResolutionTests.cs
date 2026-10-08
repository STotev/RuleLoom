using System.Text.Json;
using System.Text.Json.Nodes;
using RuleForge.Abstractions;

namespace RuleForge.Tests;

public class JsonStateResolutionTests
{
    private const string OrderJson = """
    {
      "Number": "SO-1042",
      "Total": 19.99,
      "Quantity": 3,
      "Paid": true,
      "Note": null,
      "Customer": { "Name": "Ada", "Address": { "City": "Paris" } },
      "Lines": [{ "Sku": "A-1" }]
    }
    """;

    private static readonly PathResolver Resolver = new();

    public static TheoryData<object> JsonRoots() =>
    [
        JsonDocument.Parse(OrderJson).RootElement,
        JsonSerializer.Deserialize<object>(OrderJson)!,
        JsonNode.Parse(OrderJson)!
    ];

    [Theory]
    [MemberData(nameof(JsonRoots))]
    public void Resolve_JsonState_ReturnsClrValues(object root)
    {
        Assert.Equal("SO-1042", Resolver.Resolve(root, "Number"));
        Assert.Equal(19.99m, Resolver.Resolve(root, "Total"));
        Assert.Equal(3L, Resolver.Resolve(root, "Quantity"));
        Assert.Equal(true, Resolver.Resolve(root, "Paid"));
        Assert.Null(Resolver.Resolve(root, "Note"));
    }

    [Theory]
    [MemberData(nameof(JsonRoots))]
    public void Resolve_NestedJsonPath_ResolvesValue(object root)
    {
        Assert.Equal("Paris", Resolver.Resolve(root, "Customer.Address.City"));
    }

    [Theory]
    [MemberData(nameof(JsonRoots))]
    public void Resolve_KeyInAnyCase_ResolvesValue(object root)
    {
        Assert.Equal("Ada", Resolver.Resolve(root, "customer.NAME"));
    }

    [Theory]
    [MemberData(nameof(JsonRoots))]
    public void Resolve_MissingOrNonObjectSegments_ReturnsNull(object root)
    {
        Assert.Null(Resolver.Resolve(root, "Missing"));
        Assert.Null(Resolver.Resolve(root, "Customer.Missing.City"));
        Assert.Null(Resolver.Resolve(root, "Number.Length"));
        Assert.Null(Resolver.Resolve(root, "Note.Value"));
        Assert.Null(Resolver.Resolve(root, "Lines.Sku"));
        Assert.Null(Resolver.Resolve(root, "Lines.0"));
    }

    [Fact]
    public void Resolve_ObjectsAndArrays_ReturnJson()
    {
        var element = JsonDocument.Parse(OrderJson).RootElement;
        var node = JsonNode.Parse(OrderJson)!;

        Assert.Equal(JsonValueKind.Object, Assert.IsType<JsonElement>(Resolver.Resolve(element, "Customer")).ValueKind);
        Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(Resolver.Resolve(element, "Lines")).ValueKind);
        Assert.IsType<JsonObject>(Resolver.Resolve(node, "Customer"));
        Assert.IsType<JsonArray>(Resolver.Resolve(node, "Lines"));
    }

    [Fact]
    public void Resolve_EmptyPath_ReturnsJsonRoot()
    {
        var element = JsonDocument.Parse(OrderJson).RootElement;

        Assert.Equal(element, Resolver.Resolve(element, ""));
    }

    [Theory]
    [InlineData("""{ "Price": 1, "price": 2 }""", "Price", 1L)]
    [InlineData("""{ "Price": 1, "price": 2 }""", "price", 2L)]
    [InlineData("""{ "Price": 1, "price": 2 }""", "PRICE", null)]
    [InlineData("""{ "Price": 1, "Price": 2 }""", "Price", null)]
    [InlineData("""{ "price": 1 }""", "Price", 1L)]
    public void Resolve_KeyMatching_ExactCaseWinsAndAmbiguityIsMissing(string json, string path, object? expected)
    {
        Assert.Equal(expected, Resolver.Resolve(JsonDocument.Parse(json).RootElement, path));
    }

    [Fact]
    public void Resolve_JsonObjectKeysDifferingInCase_ExactCaseWins()
    {
        var node = new JsonObject { ["Price"] = 1, ["price"] = 2 };

        Assert.Equal(1, Resolver.Resolve(node, "Price"));
        Assert.Equal(2, Resolver.Resolve(node, "price"));
        Assert.Null(Resolver.Resolve(node, "PRICE"));
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("-7", -7L)]
    [InlineData("1.0", "1.0")]
    [InlineData("1e2", "100")]
    [InlineData("0.1", "0.1")]
    [InlineData("12345678901234567890", "12345678901234567890")]
    public void Resolve_JsonNumbers_MapToLongOrDecimal(string json, object expected)
    {
        var value = Resolver.Resolve(JsonDocument.Parse($$"""{ "N": {{json}} }""").RootElement, "N");

        Assert.Equal(expected is string text ? decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : expected, value);
    }

    [Fact]
    public void Resolve_NumberBeyondDecimalRange_ReturnsDouble()
    {
        Assert.Equal(1e300, Resolver.Resolve(JsonDocument.Parse("""{ "N": 1e300 }""").RootElement, "N"));
    }

    [Fact]
    public void Resolve_NumberBeyondDoubleRange_ReturnsNull()
    {
        Assert.Null(Resolver.Resolve(JsonDocument.Parse("""{ "N": 1e400 }""").RootElement, "N"));
    }

    [Fact]
    public void Resolve_JsonValueFromClrValue_ReturnsItUnchanged()
    {
        var dueAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var node = new JsonObject { ["Total"] = JsonValue.Create(19.99m), ["DueAt"] = JsonValue.Create(dueAt) };

        Assert.Equal(19.99m, Resolver.Resolve(node, "Total"));
        Assert.Equal(dueAt, Resolver.Resolve(node, "DueAt"));
    }

    [Fact]
    public void Resolve_PropertyDeclaredAsJson_ContinuesIntoJson()
    {
        var envelope = new ElementEnvelope { Payload = JsonDocument.Parse(OrderJson).RootElement };
        var nodeEnvelope = new NodeEnvelope { Payload = JsonNode.Parse(OrderJson) };

        Assert.Equal("Ada", Resolver.Resolve(envelope, "Payload.Customer.Name"));
        Assert.Equal("Ada", Resolver.Resolve(nodeEnvelope, "payload.customer.name"));
        Assert.Equal("t-1", Resolver.Resolve(envelope, "TenantId"));
    }

    [Fact]
    public void Resolve_PropertyDeclaredAsObject_UsesRuntimeValue()
    {
        var json = new ObjectEnvelope { Payload = JsonDocument.Parse(OrderJson).RootElement };
        var clr = new ObjectEnvelope { Payload = new Customer { Name = "Grace", Address = new Address { City = "Rome" } } };

        Assert.Equal("Paris", Resolver.Resolve(json, "Payload.Customer.Address.City"));
        Assert.Equal("Rome", Resolver.Resolve(clr, "Payload.Address.City"));
        Assert.Null(Resolver.Resolve(clr, "Payload.Missing"));
        Assert.Null(Resolver.Resolve(new ObjectEnvelope(), "Payload.Name"));
    }

    [Fact]
    public void Resolve_PropertyDeclaredAsInterface_UsesRuntimeValue()
    {
        var envelope = new InterfaceEnvelope { Payload = new Customer { Name = "Ada", Address = new Address { City = "Paris" } } };

        Assert.Equal("Paris", Resolver.Resolve(envelope, "Payload.Address.City"));
    }

    [Fact]
    public void Resolve_ClrPath_StillCompiledOncePerTypeAndPath()
    {
        var resolver = new PathResolver();
        var envelope = new ObjectEnvelope { Payload = new Customer { Name = "Ada" } };

        Assert.Equal("Ada", resolver.Resolve(envelope, "Payload.Name"));
        Assert.True(resolver.IsCached(typeof(ObjectEnvelope), "Payload.Name"));
        Assert.True(resolver.IsCached(typeof(Customer), "Name"));
    }

    [Fact]
    public void Resolve_DisposedJsonDocument_Throws()
    {
        var document = JsonDocument.Parse(OrderJson);
        var element = document.RootElement;
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => Resolver.Resolve(element, "Number"));
    }

    [Theory]
    [InlineData("$current.Customer.Name", "Ada")]
    [InlineData("Customer.Name", "Ada")]
    [InlineData("$previous.Customer.Name", "Grace")]
    public void EvaluationContext_JsonStates_SelectsState(string path, string expected)
    {
        var context = new EvaluationContext(
            JsonNode.Parse("""{ "Customer": { "Name": "Ada" } }"""),
            JsonDocument.Parse("""{ "Customer": { "Name": "Grace" } }""").RootElement);

        Assert.Equal(expected, context.Resolve(path));
    }

    [Fact]
    public void EvaluationContext_StateAlone_ReturnsWholeJson()
    {
        var current = JsonNode.Parse(OrderJson);
        var context = new EvaluationContext(current);

        Assert.Same(current, context.Resolve("$current"));
    }

    private interface IParty
    {
        string Name { get; }
    }

    private sealed class Customer : IParty
    {
        public string Name { get; init; } = string.Empty;
        public Address? Address { get; init; }
    }

    private sealed class Address
    {
        public string City { get; init; } = string.Empty;
    }

    private sealed class ElementEnvelope
    {
        public string TenantId { get; init; } = "t-1";
        public JsonElement Payload { get; init; }
    }

    private sealed class NodeEnvelope
    {
        public JsonNode? Payload { get; init; }
    }

    private sealed class ObjectEnvelope
    {
        public object? Payload { get; init; }
    }

    private sealed class InterfaceEnvelope
    {
        public IParty? Payload { get; init; }
    }
}

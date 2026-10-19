using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas.Tests;

public class SchemaJsonTests
{
    private static readonly EventSchemaDefinition Order = new OrderPlacedSchema().Build();

    [Fact]
    public void ToJson_WritesAJsonSchemaWithRuleLoomKeywords()
    {
        var document = JsonNode.Parse(Order.ToJson())!.AsObject();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)document["$schema"]);
        Assert.Equal("OrderPlaced", (string?)document["x-ruleloom-event"]);
        Assert.Equal("Order placed", (string?)document["title"]);
        Assert.Equal("object", (string?)document["type"]);
        Assert.Equal("Order total", (string?)document["properties"]!["total"]!["title"]);
        Assert.Equal("date-time", (string?)document["properties"]!["placedAt"]!["format"]);
        Assert.Equal("customerId", (string?)document["x-ruleloom-contexts"]!["customer"]!["x-ruleloom-referenced-by"]);
        Assert.Equal("address.postcode", (string?)document["x-ruleloom-contexts"]!["shipping-zone"]!["x-ruleloom-referenced-by"]!["postcode"]);
    }

    [Fact]
    public void ToJson_RepeatedTypeInTheSubject_IsAReference()
    {
        Assert.Contains("\"$ref\": \"#\"", Order.ToJson());
    }

    [Fact]
    public void Parse_RoundTripsWithoutLoss()
    {
        var parsed = EventSchemaDefinition.Parse(Order.ToJson());

        Assert.Equal(Order.ToJson(), parsed.ToJson());
        Assert.Equal((Order.EventType, Order.Label, Order.Description), (parsed.EventType, parsed.Label, parsed.Description));
        Assert.Equal(Order.Fields, parsed.Fields);
        Assert.Equal(Order.Contexts, parsed.Contexts);
    }

    [Fact]
    public void ParseMany_ReadsACollection()
    {
        var json = new SchemaCollection().Add<OrderPlacedSchema>().Add<LineAddedSchema>().ToJson();

        var schemas = SchemaSet.Parse(json).Events;

        Assert.Equal(["OrderPlaced", "LineAdded"], schemas.Select(s => s.EventType));
    }

    [Fact]
    public void Parse_UnknownKeywords_AreIgnored()
    {
        var schema = EventSchemaDefinition.Parse("""
            { "x-ruleloom-event": "Pinged", "$comment": "hi", "type": "object",
              "properties": { "at": { "type": "string", "format": "date-time", "examples": ["2026-01-01T00:00:00Z"] } } }
            """);

        Assert.Equal(("Pinged", (string?)null), (schema.EventType, schema.Label));
        Assert.Equal((FieldType.DateTime, (string?)null), (schema.Fields[0].Type, schema.Fields[0].Label));
    }

    [Theory]
    [InlineData("""{ "type": "object" }""")]
    [InlineData("""[]""")]
    [InlineData("""{ "x-ruleloom-event": "E", "properties": { "a": { "type": "date" } } }""")]
    [InlineData("""{ "x-ruleloom-event": "E", "x-ruleloom-contexts": [] }""")]
    [InlineData("""{ "x-ruleloom-event": "E", "x-ruleloom-contexts": { "c": { "x-ruleloom-referenced-by": 1 } } }""")]
    [InlineData("""{ "x-ruleloom-event": " " }""")]
    [InlineData("""{ "x-ruleloom-event": "E", "x-ruleloom-contexts": { "c": { "x-ruleloom-provider": "" } } }""")]
    [InlineData("""{ "x-ruleloom-event": "E", "x-ruleloom-contexts": { "customer": {}, "Customer": {} } }""")]
    public void Parse_InvalidDocument_Throws(string json)
    {
        Assert.ThrowsAny<JsonException>(() => EventSchemaDefinition.Parse(json));
    }
}

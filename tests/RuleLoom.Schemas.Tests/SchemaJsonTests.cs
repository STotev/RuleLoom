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
        Assert.Equal("array", (string?)document["x-ruleloom-contexts"]!["watchers"]!["type"]);
    }

    [Fact]
    public void ToJson_ReferencesInContexts_PointInsideTheirContext()
    {
        var document = JsonNode.Parse(Order.ToJson())!;
        var contexts = document["x-ruleloom-contexts"]!.AsObject();
        var checkedReferences = 0;

        foreach (var (name, context) in contexts)
        {
            foreach (var reference in References(context))
            {
                Assert.StartsWith($"#/x-ruleloom-contexts/{name}", reference);
                Assert.NotNull(Resolve(document, reference));
                checkedReferences++;
            }
        }

        Assert.True(checkedReferences >= 2);
    }

    [Fact]
    public void Parse_RoundTripsWithoutLoss()
    {
        var parsed = EventSchemaDefinition.Parse(Order.ToJson());

        Assert.Equal(Order.ToJson(), parsed.ToJson());
        Assert.Equal((Order.EventType, Order.Label, Order.Description), (parsed.EventType, parsed.Label, parsed.Description));
        Assert.Equal(Describe(Order.Fields), Describe(parsed.Fields));
        Assert.Equal(Order.Contexts.Select(c => (c.Name, c.Label, c.IsMany, Describe(c.Fields))),
            parsed.Contexts.Select(c => (c.Name, c.Label, c.IsMany, Describe(c.Fields))));
    }

    [Fact]
    public void ParseMany_ReadsACollection()
    {
        var json = new EventSchemaCollection().Add<OrderPlacedSchema>().Add<LineAddedSchema>().ToJson();

        var schemas = EventSchemaDefinition.ParseMany(json);

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
    public void Parse_InvalidDocument_Throws(string json)
    {
        Assert.ThrowsAny<JsonException>(() => EventSchemaDefinition.Parse(json));
    }

    private static IEnumerable<string> References(JsonNode? node) => node switch
    {
        JsonObject obj => (obj["$ref"] is JsonValue value ? [value.GetValue<string>()] : Enumerable.Empty<string>())
            .Concat(obj.SelectMany(pair => References(pair.Value))),
        JsonArray array => array.SelectMany(References),
        _ => []
    };

    private static JsonNode? Resolve(JsonNode root, string pointer) =>
        pointer.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Aggregate<string, JsonNode?>(root, (node, segment) => node?[segment.Replace("~1", "/").Replace("~0", "~")]);

    private static string Describe(IEnumerable<FieldDefinition> fields) =>
        string.Join(";", fields.Select(f =>
            $"{f.Name}:{f.Type}:{f.IsNullable}:{f.Label}:{f.Description}:{string.Join("|", f.EnumValues)}" +
            $"[{Describe(f.Fields)}]<{(f.Items is null ? "" : Describe([f.Items]))}>"));
}

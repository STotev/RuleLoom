using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RuleLoom.Schemas.Tests;

/// <summary>
/// How the schema exporter's output for untyped values, nullable enums, repeated types and number handling is read.
/// </summary>
public class ExporterEdgeCaseTests
{
    [Fact]
    public void Build_UntypedProperties_AreAnyFields()
    {
        var schema = new PayloadSchema().Build();

        Assert.Equal(FieldType.Any, Field(schema.Fields, "payload").Type);
        Assert.Equal(FieldType.Any, Field(schema.Fields, "data").Type);
        Assert.Equal(FieldType.Any, Field(schema.Fields, "bag").Items!.Type);
    }

    [Fact]
    public void Build_UntypedPropertyWithLabel_IsDescribedAndStaysNullable()
    {
        var payload = Field(new PayloadSchema().Build().Fields, "payload");

        Assert.Equal(("Raw payload", FieldType.Any, true), (payload.Label, payload.Type, payload.IsNullable));
    }

    [Fact]
    public void Build_NamedFloatingPointLiterals_StayNumbers()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

        Assert.Equal(FieldType.Number, Field(new PayloadSchema().Build(options).Fields, "score").Type);
    }

    [Fact]
    public void Build_OptionsChangedAfterABuild_AreFollowed()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var schema = new PayloadSchema();

        var before = schema.Build(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        var after = schema.Build(options);

        Assert.Contains(before.Fields, f => f.Name == "maybeStatus");
        Assert.Contains(after.Fields, f => f.Name == "maybe_status");
    }

    [Fact]
    public void Build_CompositeReferencePartsThatCollide_Throw()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new CollidingReferenceSchema().Build());

        Assert.Contains("context 'thing'", error.Message);
    }

    [Fact]
    public void Build_NullableStringEnum_IsNullable()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        var status = Field(new PayloadSchema().Build(options).Fields, "maybeStatus");

        Assert.Equal((FieldType.Enum, true), (status.Type, status.IsNullable));
        Assert.Equal(["Placed", "Paid"], status.EnumValues);
    }

    [Fact]
    public void Build_NumbersWrittenAsStrings_AreStrings()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.WriteAsString };

        Assert.Equal(FieldType.String, Field(new PayloadSchema().Build(options).Fields, "quantity").Type);
    }

    [Fact]
    public void Build_RepeatedTypeInASibling_IsDescribedInFull()
    {
        var schema = new TreeSchema().Build();
        var kids = Field(Field(schema.Fields, "b").Fields, "kids");

        Assert.Equal(FieldType.Array, kids.Type);
        Assert.Equal(["name", "kids"], kids.Items!.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Build_IgnoredFieldReferencedElsewhere_LeavesNoDanglingReference()
    {
        var document = JsonNode.Parse(new TreeWithoutASchema().Build().ToJson())!;

        Assert.Null(document["properties"]!["a"]);
        Assert.All(References(document), reference => Assert.NotNull(Resolve(document, reference)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_FieldInsideAnIgnoredField_Throws(bool ignoreFirst)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new InsideIgnoredSchema(ignoreFirst).Build());

        Assert.Contains("inside ignored field 'Customer'", error.Message);
    }

    [Theory]
    [InlineData("""{ "x-ruleloom-event": "E", "properties": { "a": { "type": 1 } } }""")]
    [InlineData("""{ "x-ruleloom-event": "E", "properties": {}, "properties": {} }""")]
    [InlineData("""{ "x-ruleloom-event": "E", "properties": { "a": { "$ref": "#/nowhere" } } }""")]
    [InlineData("""{ "x-ruleloom-event": "E", "properties": { "a": 5 } }""")]
    public void Parse_MalformedDocument_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => EventSchemaDefinition.Parse(json));
    }

    private static FieldDefinition Field(IEnumerable<FieldDefinition> fields, string name) => Assert.Single(fields, f => f.Name == name);

    private static IEnumerable<string> References(JsonNode? node) => node switch
    {
        JsonObject obj => (obj["$ref"] is JsonValue value ? [value.GetValue<string>()] : Enumerable.Empty<string>())
            .Concat(obj.SelectMany(pair => References(pair.Value))),
        JsonArray array => array.SelectMany(References),
        _ => []
    };

    private static JsonNode? Resolve(JsonNode root, string pointer) =>
        pointer.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Aggregate<string, JsonNode?>(root, (node, segment) => node?[segment]);

    private sealed class PayloadSchema : EventSchema<PayloadDto>
    {
        public PayloadSchema()
        {
            Event("PayloadReceived");
            Field(p => p.Payload).Label("Raw payload");
        }
    }

    private sealed class CollidingReferenceSchema : EventSchema<PayloadDto>
    {
        public CollidingReferenceSchema()
        {
            Event("Colliding");
            Context("thing").ReferencedBy(p => new { code = p.Code, Code = p.Quantity });
        }
    }

    private sealed class TreeSchema : EventSchema<TreeDto>
    {
        public TreeSchema() => Event("TreeChanged");
    }

    private sealed class TreeWithoutASchema : EventSchema<TreeDto>
    {
        public TreeWithoutASchema()
        {
            Event("TreeChangedWithoutA");
            Field(t => t.A).Ignore();
        }
    }

    private sealed class InsideIgnoredSchema : EventSchema<OrderDto>
    {
        public InsideIgnoredSchema(bool ignoreFirst)
        {
            Event("InsideIgnored");
            if (ignoreFirst)
                Field(o => o.Customer).Ignore();
            Field(o => o.Customer.Tier).Label("Tier");
            if (!ignoreFirst)
                Field(o => o.Customer).Ignore();
        }
    }
}

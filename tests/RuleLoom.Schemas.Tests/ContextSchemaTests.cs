using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas.Tests;

public class ContextSchemaTests
{
    private static readonly ContextSchemaDefinition Account = Contexts.Account();
    private static readonly ContextSchemaDefinition Watchers = Contexts.Watchers();

    [Fact]
    public void Build_DescribesTheContextAndItsFields()
    {
        Assert.Equal(("account", "Account", "A customer's account.", false), (Account.Name, Account.Label, Account.Description, Account.IsMany));
        Assert.Equal(["id", "balance"], Account.Fields.Select(f => f.Name));
        Assert.Equal(("Balance", FieldType.Number), (Account.Fields[1].Label, Account.Fields[1].Type));
    }

    [Fact]
    public void Build_Many_DescribesOneItemOfACollection()
    {
        var document = JsonNode.Parse(Watchers.ToJson())!;

        Assert.True(Watchers.IsMany);
        Assert.Equal(["email", "manager"], Watchers.Fields.Select(f => f.Name));
        Assert.Equal("array", (string?)document["type"]);
        Assert.Equal("watchers", (string?)document["x-ruleloom-context"]);
    }

    [Fact]
    public void Build_Many_RepeatedTypesPointInsideTheItems()
    {
        var document = JsonNode.Parse(Watchers.ToJson())!;
        var references = References(document).ToList();

        Assert.NotEmpty(references);
        Assert.All(references, reference =>
        {
            Assert.StartsWith("#/items", reference);
            Assert.NotNull(Resolve(document, reference));
        });
    }

    [Fact]
    public void Parse_RoundTripsWithoutLoss()
    {
        var parsed = ContextSchemaDefinition.Parse(Account.ToJson());

        Assert.Equal(Account.ToJson(), parsed.ToJson());
        Assert.Equal(Account.Fields, parsed.Fields);
        Assert.Equal((Account.Name, Account.Label, Account.IsMany), (parsed.Name, parsed.Label, parsed.IsMany));
        Assert.Equal(Watchers.Fields, ContextSchemaDefinition.Parse(Watchers.ToJson()).Fields);
    }

    [Theory]
    [InlineData("""{ "type": "object" }""")]
    [InlineData("""{ "x-ruleloom-context": " " }""")]
    [InlineData("""{ "x-ruleloom-context": "c", "type": "array" }""")]
    [InlineData("""{ "x-ruleloom-context": "c", "properties": { "a": { "type": 1 } } }""")]
    public void Parse_MalformedDocument_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => ContextSchemaDefinition.Parse(json));
    }

    [Fact]
    public void Build_WithoutConfiguration_DescribesTheTypeWithoutLabels()
    {
        var schema = new PlainAccountSchema().Build("account");

        Assert.Equal((null, null), (schema.Label, schema.Description));
        Assert.Equal(["id", "balance", "internalRiskScore"], schema.Fields.Select(f => f.Name));
        Assert.All(schema.Fields, field => Assert.Null(field.Label));
    }

    [Fact]
    public void Build_CollectionType_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new UserListSchema().Build("watchers"));

        Assert.Contains("collection", error.Message);
    }

    [Fact]
    public void Build_FieldNotSerialized_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new SecretOrderSchema().Build("order"));
    }

    [Fact]
    public void Build_DictionaryType_IsNotACollection()
    {
        var schema = new SettingsSchema().Build("settings");

        Assert.False(schema.IsMany);
        Assert.Empty(schema.Fields);
    }

    [Fact]
    public void DataType_IsTheDescribedType()
    {
        Assert.Equal(typeof(AccountDto), ((IContextSchema)new AccountSchema()).DataType);
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
            .Aggregate<string, JsonNode?>(root, (node, segment) => node?[segment]);
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas.Tests;

public class CatalogDocumentTests
{
    [Fact]
    public void ToJson_Parse_RoundTripsEverySection()
    {
        var document = new CatalogDocument(
            new SchemaCollection().Add<OrderPlacedSchema>().Build().Events,
            [Contexts.Account(), Contexts.Watchers()],
            [new CatalogOperator("eq", "equals", "Equal values.", 2, 2), new CatalogOperator("and", null, null, 1, null)],
            [new CatalogAction("send-email", "Send email", null)],
            [new CatalogProblem(CatalogEntryKind.Event, "Shipped", "Broken."), new CatalogProblem(CatalogEntryKind.Context, "crm", "No provider.")]);

        var json = document.ToJson();
        var parsed = CatalogDocument.Parse(json);

        Assert.Equal(["OrderPlaced"], parsed.Events.Select(e => e.EventType));
        Assert.Equal(["account", "watchers"], parsed.Contexts.Select(c => c.Name));
        Assert.Equal(document.Operators, parsed.Operators);
        Assert.Equal(document.Actions, parsed.Actions);
        Assert.Equal(document.Problems, parsed.Problems);
        Assert.Equal(json, parsed.ToJson());
    }

    [Fact]
    public void ToJson_WritesSectionsAndOmitsMissingValues()
    {
        var json = JsonNode.Parse(new CatalogDocument(operators: [new CatalogOperator("and", null, null, 1, null)]).ToJson())!;

        Assert.Equal(["events", "contexts", "operators", "actions", "problems"], json.AsObject().Select(p => p.Key));
        Assert.Equal("""{"name":"and","minOperands":1}""", json["operators"]![0]!.ToJsonString());
    }

    [Fact]
    public void Parse_MissingSections_AreEmpty()
    {
        var parsed = CatalogDocument.Parse("""{ "actions": [ { "name": "notify" } ] }""");

        Assert.Empty(parsed.Events);
        Assert.Equal([new CatalogAction("notify", null, null)], parsed.Actions);
    }

    [Fact]
    public void Constructor_SameNameTwiceInAKind_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new CatalogDocument(contexts: [Contexts.Account(), Contexts.Account()]));

        Assert.Contains("account", error.Message);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{ "events": {} }""")]
    [InlineData("""{ "events": [ { "type": "object" } ] }""")]
    [InlineData("""{ "contexts": [ { "x-ruleloom-context": "a" }, { "x-ruleloom-context": "A" } ] }""")]
    [InlineData("""{ "operators": [ { "label": "no name" } ] }""")]
    [InlineData("""{ "operators": [ { "name": "eq", "minOperands": "two" } ] }""")]
    [InlineData("""{ "problems": [ { "kind": "rule", "name": "x" } ] }""")]
    public void Parse_InvalidDocuments_ThrowJsonException(string json)
    {
        Assert.Throws<JsonException>(() => CatalogDocument.Parse(json));
    }
}

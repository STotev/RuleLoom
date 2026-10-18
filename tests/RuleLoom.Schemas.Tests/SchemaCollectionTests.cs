namespace RuleLoom.Schemas.Tests;

public class SchemaCollectionTests
{
    [Fact]
    public void AddFromAssemblyOf_AddsPublicSchemaClassesOrderedByName()
    {
        var schemas = new SchemaCollection().AddFromAssemblyOf<OrderPlacedSchema>().Build().Events;

        Assert.Equal(["LineAdded", "OrderPlaced"], schemas.Select(s => s.EventType));
    }

    [Fact]
    public void Build_SameEventTwice_Throws()
    {
        var collection = new SchemaCollection().Add<LineAddedSchema>().Add(new LineAddedSchema());

        var error = Assert.Throws<InvalidOperationException>(() => collection.Build());

        Assert.Contains("LineAdded", error.Message);
    }

    [Fact]
    public void AddFromAssemblyOf_AddsContextSchemaClassesToo()
    {
        var schemas = new SchemaCollection().AddFromAssemblyOf<AccountSchema>().Build();

        Assert.Equal(["account", "watchers"], schemas.Contexts.Select(c => c.Name));
    }

    [Fact]
    public void Build_SameContextTwice_Throws()
    {
        var collection = new SchemaCollection().Add<AccountSchema>().Add(new AccountSchema());

        Assert.Contains("account", Assert.Throws<InvalidOperationException>(() => collection.Build()).Message);
    }

    [Fact]
    public void Add_TypeThatIsNotASchema_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SchemaCollection().Add<AccountDto>());
    }

    [Fact]
    public void SchemaSet_RoundTripsEventsAndContexts()
    {
        var json = new SchemaCollection().Add<OrderPlacedSchema>().Add<AccountSchema>().Add<WatchersSchema>().ToJson();

        var parsed = SchemaSet.Parse(json);

        Assert.Equal(["OrderPlaced"], parsed.Events.Select(e => e.EventType));
        Assert.Equal(["account", "watchers"], parsed.Contexts.Select(c => c.Name));
        Assert.Equal(json, parsed.ToJson());
    }

    [Theory]
    [InlineData("""[{ "type": "object" }]""")]
    [InlineData("""[{ "x-ruleloom-context": "a" }, { "x-ruleloom-context": "A" }]""")]
    [InlineData("""{ "x-ruleloom-context": "a" }""")]
    public void SchemaSet_Parse_InvalidDocuments_Throw(string json)
    {
        Assert.Throws<System.Text.Json.JsonException>(() => SchemaSet.Parse(json));
    }
}

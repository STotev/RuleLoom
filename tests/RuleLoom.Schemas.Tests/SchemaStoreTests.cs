namespace RuleLoom.Schemas.Tests;

public class SchemaStoreTests
{
    private static readonly EventSchemaDefinition Order = new OrderPlacedSchema().Build();
    private static readonly EventSchemaDefinition Line = new LineAddedSchema().Build();

    [Fact]
    public async Task InMemorySchemaStore_GetsListsAndReplaces()
    {
        var store = new InMemorySchemaStore([Order]);

        await store.SaveAsync(Line, TestContext.Current.CancellationToken);
        await store.SaveAsync(EventSchemaDefinition.Parse(Line.ToJson()), TestContext.Current.CancellationToken);

        Assert.Same(Order, await store.GetAsync("orderplaced", TestContext.Current.CancellationToken));
        Assert.Null(await store.GetAsync("Unknown", TestContext.Current.CancellationToken));
        Assert.Equal(["LineAdded", "OrderPlaced"], (await store.ListAsync(TestContext.Current.CancellationToken)).Select(s => s.EventType));
    }

    [Fact]
    public async Task InMemorySchemaStore_ImportedCollection_GivesBackTheSameSchemas()
    {
        var json = new EventSchemaCollection().Add<OrderPlacedSchema>().Add<LineAddedSchema>().ToJson();

        var store = new InMemorySchemaStore(EventSchemaDefinition.ParseMany(json));
        var stored = await store.GetAsync("OrderPlaced", TestContext.Current.CancellationToken);

        Assert.Equal(Order.Fields, stored!.Fields);
        Assert.Equal(Order.Contexts, stored.Contexts);
    }
}

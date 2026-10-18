namespace RuleLoom.Schemas.Tests;

public class SchemaStoreTests
{
    private static readonly EventSchemaDefinition Order = new OrderPlacedSchema().Build();
    private static readonly EventSchemaDefinition Line = new LineAddedSchema().Build();

    [Fact]
    public async Task InMemorySchemaStore_GetsListsAndReplaces()
    {
        var store = new InMemorySchemaStore(new SchemaSet([Order], []));

        await store.SaveEventAsync(Line, TestContext.Current.CancellationToken);
        await store.SaveEventAsync(EventSchemaDefinition.Parse(Line.ToJson()), TestContext.Current.CancellationToken);

        Assert.Same(Order, await store.GetEventAsync("orderplaced", TestContext.Current.CancellationToken));
        Assert.Null(await store.GetEventAsync("Unknown", TestContext.Current.CancellationToken));
        Assert.Equal(["LineAdded", "OrderPlaced"], (await store.ListEventsAsync(TestContext.Current.CancellationToken)).Select(s => s.EventType));
    }

    [Fact]
    public async Task InMemorySchemaStore_ImportedCollection_GivesBackTheSameSchemas()
    {
        var json = new SchemaCollection().Add<OrderPlacedSchema>().Add<LineAddedSchema>().ToJson();

        var store = new InMemorySchemaStore(SchemaSet.Parse(json));
        var stored = await store.GetEventAsync("OrderPlaced", TestContext.Current.CancellationToken);

        Assert.Equal(Order.Fields, stored!.Fields);
        Assert.Equal(Order.Contexts, stored.Contexts);
    }

    [Fact]
    public async Task InMemorySchemaStore_KeepsContextSchemas()
    {
        var account = new AccountSchema().Build();
        var store = new InMemorySchemaStore(new SchemaSet([], [account]));
        var ct = TestContext.Current.CancellationToken;

        await store.SaveContextAsync(new WatchersSchema().Build(), ct);
        await store.SaveContextAsync(ContextSchemaDefinition.Parse(account.ToJson()), ct);

        Assert.Equal("account", (await store.GetContextAsync("ACCOUNT", ct))!.Name);
        Assert.Null(await store.GetContextAsync("unknown", ct));
        Assert.Equal(["account", "watchers"], (await store.ListContextsAsync(ct)).Select(c => c.Name));
    }
}

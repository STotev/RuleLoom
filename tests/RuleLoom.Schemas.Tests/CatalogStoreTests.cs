namespace RuleLoom.Schemas.Tests;

public class CatalogStoreTests
{
    private static readonly EventSchemaDefinition Order = new OrderPlacedSchema().Build();
    private static readonly EventSchemaDefinition Line = new LineAddedSchema().Build();

    [Fact]
    public async Task LoadAsync_NothingSaved_ReturnsAnEmptyDocument()
    {
        var document = await new InMemoryCatalogStore().LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(document.Events);
        Assert.Empty(document.Contexts);
        Assert.Empty(document.Operators);
        Assert.Empty(document.Actions);
    }

    [Fact]
    public async Task SaveAsync_UpsertsEntriesByKindAndName_AndNeverDeletes()
    {
        var store = new InMemoryCatalogStore();
        var ct = TestContext.Current.CancellationToken;

        await store.SaveAsync(new CatalogDocument([Order], [Contexts.Account()], [new CatalogOperator("eq", null, null, 2, 2)],
            [new CatalogAction("send-email", "Send email", null)]), ct);
        await store.SaveAsync(new CatalogDocument([Line, EventSchemaDefinition.Parse(Order.ToJson())],
            actions: [new CatalogAction("SEND-EMAIL", "Send an email", null)]), ct);

        var document = await store.LoadAsync(ct);

        Assert.Equal(["OrderPlaced", "LineAdded"], document.Events.Select(e => e.EventType));
        Assert.NotSame(Order, document.Events[0]);
        Assert.Equal(["account"], document.Contexts.Select(c => c.Name));
        Assert.Equal(["eq"], document.Operators.Select(o => o.Name));
        Assert.Equal([new CatalogAction("SEND-EMAIL", "Send an email", null)], document.Actions);
    }

    [Fact]
    public async Task SaveAsync_DoesNotStoreProblems()
    {
        var store = new InMemoryCatalogStore();
        var ct = TestContext.Current.CancellationToken;

        await store.SaveAsync(new CatalogDocument(problems: [new CatalogProblem(CatalogEntryKind.Context, "account", "No provider.")]), ct);

        Assert.Empty((await store.LoadAsync(ct)).Problems);
    }
}

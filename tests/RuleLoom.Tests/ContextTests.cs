using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Context;
using RuleLoom.Contexts;

namespace RuleLoom.Tests;

public class ContextTests
{
    private static readonly object Order = new { CustomerId = "c-1", Total = 640.5m };

    [Fact]
    public async Task GetContextAsync_CalledTwice_LoadsOnce()
    {
        var provider = new FakeContextProvider(new { Tier = "Gold" });
        var context = Context(("customer", provider));

        await context.GetContextAsync("customer", TestContext.Current.CancellationToken);
        await context.GetContextAsync("CUSTOMER", TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task GetContextAsync_PassesTheEventToTheProvider()
    {
        var provider = new FakeContextProvider(request => request.Resolve("CustomerId"));
        var context = Context(("customer", provider));

        var value = await context.GetContextAsync("customer", TestContext.Current.CancellationToken);

        Assert.Equal("c-1", value);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(("customer", "OrderPlaced"), (request.Name, request.EventType));
        Assert.Same(Order, request.Current);
    }

    [Fact]
    public async Task Resolve_LoadedContext_ResolvesPathsOnIt()
    {
        var context = Context(
            ("customer", new FakeContextProvider(new { Tier = "Gold", Address = new { City = "Paris" } })),
            ("account", new FakeContextProvider(JsonNode.Parse("""{ "Manager": { "Email": "m@example.com" } }"""))));

        await context.GetContextAsync("customer", TestContext.Current.CancellationToken);
        await context.GetContextAsync("account", TestContext.Current.CancellationToken);

        Assert.Equal("Gold", context.Resolve("@customer.Tier"));
        Assert.Equal("Paris", context.Resolve("@Customer.address.city"));
        Assert.Equal("m@example.com", context.Resolve("@account.Manager.Email"));
        Assert.Null(context.Resolve("@customer.Missing"));
    }

    [Fact]
    public async Task Resolve_CollectionContext_ReturnsTheCollection()
    {
        string[] watchers = ["a@example.com", "b@example.com"];
        var context = Context(("watchers", new FakeContextProvider(watchers)));

        await context.GetContextAsync("watchers", TestContext.Current.CancellationToken);

        Assert.Same(watchers, context.Resolve("@watchers"));
        Assert.Null(context.Resolve("@watchers.Email"));
    }

    [Fact]
    public async Task Resolve_ContextNotFound_ResolvesToNull()
    {
        var context = Context(("customer", new FakeContextProvider((object?)null)));

        Assert.Null(await context.GetContextAsync("customer", TestContext.Current.CancellationToken));
        Assert.Null(context.Resolve("@customer.Tier"));
    }

    [Fact]
    public async Task Resolve_FailedContext_ThrowsContextLoadException()
    {
        var context = Context(("customer", FakeContextProvider.Failing("customers service is down")));

        var loadError = await Assert.ThrowsAsync<ContextLoadException>(
            async () => await context.GetContextAsync("customer", TestContext.Current.CancellationToken));
        var resolveError = Assert.Throws<ContextLoadException>(() => context.Resolve("@customer.Tier"));

        Assert.Equal("customer", resolveError.Name);
        Assert.Equal("customers service is down", resolveError.InnerException!.Message);
        Assert.Same(loadError, resolveError);
    }

    [Fact]
    public void Resolve_UnregisteredContext_Throws()
    {
        var context = Context(("customer", new FakeContextProvider(new { })));

        var error = Assert.Throws<InvalidOperationException>(() => context.Resolve("@warehouse.City"));

        Assert.Contains("No context provider is registered for '@warehouse'", error.Message);
    }

    [Fact]
    public void Resolve_ContextNotLoaded_Throws()
    {
        var context = Context(("customer", new FakeContextProvider(new { })));

        var error = Assert.Throws<InvalidOperationException>(() => context.Resolve("@customer.Tier"));

        Assert.Contains("hasn't been loaded", error.Message);
    }

    [Fact]
    public void Resolve_ContextPathWithoutProviders_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new EvaluationContext(Order).Resolve("@customer.Tier"));
    }

    [Fact]
    public async Task GetContextAsync_Cancelled_Throws()
    {
        var context = Context(("customer", new FakeContextProvider(new { })));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await context.GetContextAsync("customer", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ContextProviderBaseClass_LoadsTypedContext()
    {
        var context = Context(("customer", new CustomerProvider()));

        var customer = await context.GetContextAsync<Customer>("customer", TestContext.Current.CancellationToken);

        Assert.Equal(new Customer("c-1", "Gold"), customer);
        Assert.Equal("Gold", context.Resolve("@customer.Tier"));
    }

    [Fact]
    public async Task GetContextAsyncOfT_JsonContext_IsDeserializedIgnoringCase()
    {
        var context = Context(
            ("element", new FakeContextProvider(JsonDocument.Parse("""{ "id": "c-1", "TIER": "Gold" }""").RootElement)),
            ("node", new FakeContextProvider(JsonNode.Parse("""[{ "Id": "c-2", "Tier": "Basic" }]"""))));

        var customer = await context.GetContextAsync<Customer>("element", TestContext.Current.CancellationToken);
        var customers = await context.GetContextAsync<Customer[]>("node", TestContext.Current.CancellationToken);

        Assert.Equal(new Customer("c-1", "Gold"), customer);
        Assert.Equal([new Customer("c-2", "Basic")], customers!);
    }

    [Fact]
    public async Task GetContextAsyncOfT_NotFound_ReturnsDefault()
    {
        var context = Context(("customer", new FakeContextProvider((object?)null)));

        Assert.Null(await context.GetContextAsync<Customer>("customer", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetContextAsyncOfT_OtherType_ThrowsInvalidCast()
    {
        var context = Context(("customer", new FakeContextProvider(new { Tier = "Gold" })));

        await Assert.ThrowsAsync<InvalidCastException>(
            async () => await context.GetContextAsync<Customer>("customer", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CollectionContextProvider_LoadsItems()
    {
        var context = Context(("watchers", new WatchersProvider()));

        var watchers = await context.GetContextAsync<IReadOnlyList<Customer>>("watchers", TestContext.Current.CancellationToken);

        Assert.Equal([new Customer("c-1", "Gold"), new Customer("c-2", "Basic")], watchers!);
    }

    [Fact]
    public void GetSchema_TypedProvider_DescribesItsTypeUnderTheGivenName()
    {
        var schema = ((IContextProvider)new CustomerProvider()).GetSchema("buyer")!;

        Assert.Equal(("buyer", "Customer", false), (schema.Name, schema.Label, schema.IsMany));
        Assert.Equal(["id", "tier"], schema.Fields.Select(f => f.Name));
        Assert.Equal("Loyalty tier", schema.Fields[1].Label);
    }

    [Fact]
    public void GetSchema_CollectionProvider_DescribesOneItem()
    {
        var schema = ((IContextProvider)new WatchersProvider()).GetSchema("watchers")!;

        Assert.True(schema.IsMany);
        Assert.Equal(["id"], schema.Fields.Select(f => f.Name));
    }

    [Fact]
    public void GetSchema_UntypedProviders_HaveNoSchema()
    {
        Assert.Null(((IContextProvider)new FakeContextProvider(new { })).GetSchema("customer"));
        Assert.Null(((IContextProvider)new JsonProvider()).GetSchema("customer"));
    }

    [Fact]
    public void GetSchema_ProviderOfACollectionType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ((IContextProvider)new ListProvider()).GetSchema("watchers"));
    }

    [Fact]
    public void DirectContextProviderFactory_NamesIgnoreCaseAndDuplicatesThrow()
    {
        var provider = new FakeContextProvider(new { });
        var factory = new DirectContextProviderFactory().Add(provider, "customer");

        Assert.True(factory.TryGetProvider("CUSTOMER", out var found));
        Assert.Same(provider, found);
        Assert.Equal(["customer"], factory.ProviderNames);
        Assert.Throws<ArgumentException>(() => factory.Add(provider, "Customer"));
    }

    private static EvaluationContext Context(params (string Name, IContextProvider Provider)[] providers)
    {
        var factory = new DirectContextProviderFactory();
        foreach (var (name, provider) in providers)
            factory.Add(provider, name);

        return new EvaluationContext(Order, null, "OrderPlaced", factory);
    }

    private sealed record Customer(string Id, string Tier);

    private sealed class CustomerProvider : ContextProvider<Customer>
    {
        protected override ValueTask<Customer?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Customer?>(new Customer((string)request.Resolve("CustomerId")!, "Gold"));

        protected override void Describe(Schemas.ContextDescription<Customer> context)
        {
            context.Label("Customer");
            context.Field(c => c.Tier).Label("Loyalty tier");
        }
    }

    private sealed class WatchersProvider : CollectionContextProvider<Customer>
    {
        protected override ValueTask<IReadOnlyList<Customer>> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<Customer>>([new Customer("c-1", "Gold"), new Customer("c-2", "Basic")]);

        protected override void Describe(Schemas.ContextDescription<Customer> context) => context.Field(c => c.Tier).Ignore();
    }

    private sealed class JsonProvider : ContextProvider<JsonNode>
    {
        protected override ValueTask<JsonNode?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(JsonNode.Parse("{}"));
    }

    private sealed class ListProvider : ContextProvider<List<Customer>>
    {
        protected override ValueTask<List<Customer>?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<List<Customer>?>([]);
    }
}

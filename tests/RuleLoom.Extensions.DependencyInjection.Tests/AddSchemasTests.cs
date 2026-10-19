using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
using RuleLoom.Schemas;

namespace RuleLoom.Extensions.DependencyInjection.Tests;

public class AddSchemasTests
{
    [Fact]
    public async Task AddSchemas_SeedsTheInMemoryStoreFromClassesAndJson()
    {
        var json = new SchemaCollection().Add<AccountOpenedSchema>().ToJson();
        var services = new ServiceCollection().AddRuleLoom(r => r.AddSchemas(s => s.Add<TransferMadeSchema>().AddJson(json)));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var store = provider.GetRequiredService<ISchemaStore>();

        Assert.IsType<InMemorySchemaStore>(store);
        Assert.Equal(["AccountOpened", "TransferMade"], (await store.ListEventsAsync(TestContext.Current.CancellationToken)).Select(s => s.EventType));
    }

    [Fact]
    public void AddSchemas_UseStore_RegistersYourStore()
    {
        var services = new ServiceCollection().AddRuleLoom(r => r.AddSchemas(s => s.UseStore<EmptyStore>()));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<EmptyStore>(scope.ServiceProvider.GetRequiredService<ISchemaStore>());
    }

    [Fact]
    public async Task SaveRuleLoomSchemasAsync_SavesTheSourcesIntoYourStore()
    {
        var saved = new SavedSchemas();
        var services = new ServiceCollection()
            .AddSingleton(saved)
            .AddRuleLoom(r => r.AddSchemas(s => s.Add<TransferMadeSchema>().Add<AccountOpenedSchema>().UseStore<RecordingStore>()));

        using var provider = services.BuildServiceProvider();
        await provider.SaveRuleLoomSchemasAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["AccountOpened", "TransferMade"], saved.Schemas.Select(s => s.EventType).Order());
    }

    [Fact]
    public async Task AddSchemas_WithYourStore_SavesTheSourcesWhenTheHostStarts()
    {
        var saved = new SavedSchemas();
        var services = new ServiceCollection()
            .AddSingleton(saved)
            .AddRuleLoom(r => r.AddSchemas(s => s.Add<TransferMadeSchema>().UseStore<RecordingStore>()));

        using var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
            await hosted.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["TransferMade"], saved.Schemas.Select(s => s.EventType));
    }

    [Fact]
    public async Task AddSchemas_ProviderSchemas_ReachTheInMemoryStoreUnderEachName()
    {
        var services = new ServiceCollection()
            .AddScoped<Accounts>()
            .AddRuleLoom(r => r
                .AddContextProvider<AccountProvider>("account")
                .AddContextProvider<AccountProvider>("ledger")
                .AddSchemas());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var contexts = await provider.GetRequiredService<ISchemaStore>().ListContextsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["account", "ledger"], contexts.Select(c => c.Name));
        Assert.All(contexts, c =>
        {
            Assert.Equal("Account", c.Label);
            Assert.Equal(["id", "frozen"], c.Fields.Select(f => f.Name));
        });
    }

    [Fact]
    public async Task SaveRuleLoomSchemasAsync_SavesProviderSchemasAndJsonContextsIntoYourStore()
    {
        var json = new SchemaSet([], [new ContextDescription<AccountDto>().Build("branch")]).ToJson();
        var saved = new SavedSchemas();
        var services = new ServiceCollection()
            .AddSingleton(saved)
            .AddScoped<Accounts>()
            .AddRuleLoom(r => r
                .AddSchemas(s => s.AddJson(json).UseStore<RecordingStore>())
                .AddContextProvider<AccountProvider>("account"));

        using var provider = services.BuildServiceProvider();
        await provider.SaveRuleLoomSchemasAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["branch", "account"], saved.Contexts.Select(c => c.Name));
    }

    [Fact]
    public void AddSchemas_ContextDescribedByAProviderAndJson_ThrowsWhenTheStoreIsCreated()
    {
        var json = new SchemaSet([], [new ContextDescription<AccountDto>().Build("account")]).ToJson();
        var services = new ServiceCollection()
            .AddSingleton<Accounts>()
            .AddRuleLoom(r => r.AddSchemas(s => s.AddJson(json)).AddContextProvider<AccountProvider>("account"));

        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISchemaStore>());
        Assert.Contains("account", error.Message);
    }

    [Fact]
    public async Task SaveRuleLoomSchemasAsync_WithoutSources_DoesNothing()
    {
        var saved = new SavedSchemas();
        var withoutSchemas = new ServiceCollection().AddSingleton<Accounts>().AddRuleLoom(r => r.AddContextProvider<AccountProvider>("account"));
        var withoutSources = new ServiceCollection().AddSingleton(saved).AddRuleLoom(r => r.AddSchemas(s => s.UseStore<RecordingStore>()));

        using var first = withoutSchemas.BuildServiceProvider();
        using var second = withoutSources.BuildServiceProvider();
        await first.SaveRuleLoomSchemasAsync(TestContext.Current.CancellationToken);
        await second.SaveRuleLoomSchemasAsync(TestContext.Current.CancellationToken);

        Assert.Empty(saved.Schemas);
        Assert.Empty(saved.Contexts);
    }

    [Fact]
    public void AddSchemas_SameEventTwice_Throws()
    {
        var json = new SchemaCollection().Add<TransferMadeSchema>().ToJson();

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddRuleLoom(r => r.AddSchemas(s => s.Add<TransferMadeSchema>().AddJson(json))));
    }

    [Fact]
    public async Task RuleEngine_WithSchemas_LoadsBothContextsWithOneProvider()
    {
        var services = new ServiceCollection()
            .AddSingleton<Accounts>()
            .AddRuleLoom(r => r
                .AddSchemas(s => s.Add<TransferMadeSchema>())
                .AddContextProvider<AccountProvider>("account"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
        var rule = RuleJsonParser.Parse("""
            { "id": "frozen", "name": "To a frozen account", "eventType": "TransferMade",
              "condition": { "operator": "and", "operands": [
                { "operator": "eq", "operands": [{ "path": "@toAccount.frozen" }, { "value": true }] },
                { "operator": "eq", "operands": [{ "path": "@fromAccount.frozen" }, { "value": false }] } ] } }
            """);

        var result = await engine.ExecuteAsync("TransferMade", new TransferDto { FromAccountId = "A-1", ToAccountId = "A-2" }, [rule],
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
        Assert.Equal(["A-1", "A-2"], provider.GetRequiredService<Accounts>().Loaded.Order());
    }

    public sealed class TransferDto
    {
        public string FromAccountId { get; set; } = "";
        public string ToAccountId { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public sealed class TransferMadeSchema : EventSchema<TransferDto>
    {
        public TransferMadeSchema()
        {
            Event("TransferMade");
            Context("fromAccount").LoadedBy("account").ReferencedBy(t => t.FromAccountId).Label("From account");
            Context("toAccount").LoadedBy("account").ReferencedBy(t => t.ToAccountId).Label("To account");
        }
    }

    public sealed class AccountOpenedSchema : EventSchema<TransferDto>
    {
        public AccountOpenedSchema() => Event("AccountOpened");
    }

    public sealed record AccountDto(string Id, bool Frozen);

    public sealed class Accounts
    {
        public List<string> Loaded { get; } = [];
    }

    public sealed class AccountProvider(Accounts accounts) : ContextProvider<AccountDto>
    {
        protected override ValueTask<AccountDto?> LoadAsync(ContextRequest request, CancellationToken cancellationToken)
        {
            var id = request.GetReference<string>()!;
            lock (accounts.Loaded)
                accounts.Loaded.Add(id);
            return ValueTask.FromResult<AccountDto?>(new AccountDto(id, Frozen: id == "A-2"));
        }

        protected override void Describe(ContextDescription<AccountDto> context) => context.Label("Account");
    }

    public sealed class SavedSchemas
    {
        public List<EventSchemaDefinition> Schemas { get; } = [];
        public List<ContextSchemaDefinition> Contexts { get; } = [];
    }

    public sealed class RecordingStore(SavedSchemas saved) : ISchemaStore
    {
        public ValueTask<EventSchemaDefinition?> GetEventAsync(string eventType, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(saved.Schemas.FirstOrDefault(s => s.EventType == eventType));
        public ValueTask<IReadOnlyList<EventSchemaDefinition>> ListEventsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EventSchemaDefinition>>(saved.Schemas);
        public ValueTask SaveEventAsync(EventSchemaDefinition schema, CancellationToken cancellationToken = default)
        {
            saved.Schemas.Add(schema);
            return ValueTask.CompletedTask;
        }

        public ValueTask<ContextSchemaDefinition?> GetContextAsync(string name, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(saved.Contexts.FirstOrDefault(s => s.Name == name));
        public ValueTask<IReadOnlyList<ContextSchemaDefinition>> ListContextsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ContextSchemaDefinition>>(saved.Contexts);
        public ValueTask SaveContextAsync(ContextSchemaDefinition schema, CancellationToken cancellationToken = default)
        {
            saved.Contexts.Add(schema);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class EmptyStore : ISchemaStore
    {
        public ValueTask<EventSchemaDefinition?> GetEventAsync(string eventType, CancellationToken cancellationToken = default) => ValueTask.FromResult<EventSchemaDefinition?>(null);
        public ValueTask<IReadOnlyList<EventSchemaDefinition>> ListEventsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<EventSchemaDefinition>>([]);
        public ValueTask SaveEventAsync(EventSchemaDefinition schema, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<ContextSchemaDefinition?> GetContextAsync(string name, CancellationToken cancellationToken = default) => ValueTask.FromResult<ContextSchemaDefinition?>(null);
        public ValueTask<IReadOnlyList<ContextSchemaDefinition>> ListContextsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ContextSchemaDefinition>>([]);
        public ValueTask SaveContextAsync(ContextSchemaDefinition schema, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

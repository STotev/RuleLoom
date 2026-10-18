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
        var json = new EventSchemaCollection().Add<AccountOpenedSchema>().ToJson();
        var services = new ServiceCollection().AddRuleLoom(r => r.AddSchemas(s => s.Add<TransferMadeSchema>().AddJson(json)));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var store = provider.GetRequiredService<ISchemaStore>();

        Assert.IsType<InMemorySchemaStore>(store);
        Assert.Equal(["AccountOpened", "TransferMade"], (await store.ListAsync(TestContext.Current.CancellationToken)).Select(s => s.EventType));
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
    public void AddSchemas_SchemasWithYourOwnStore_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddRuleLoom(r => r.AddSchemas(s => s.Add<TransferMadeSchema>().UseStore<EmptyStore>())));

        Assert.Contains("EmptyStore", error.Message);
    }

    [Fact]
    public void AddSchemas_SameEventTwice_Throws()
    {
        var json = new EventSchemaCollection().Add<TransferMadeSchema>().ToJson();

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
    }

    public sealed class EmptyStore : ISchemaStore
    {
        public ValueTask<EventSchemaDefinition?> GetAsync(string eventType, CancellationToken cancellationToken = default) => ValueTask.FromResult<EventSchemaDefinition?>(null);
        public ValueTask<IReadOnlyList<EventSchemaDefinition>> ListAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<EventSchemaDefinition>>([]);
        public ValueTask SaveAsync(EventSchemaDefinition schema, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

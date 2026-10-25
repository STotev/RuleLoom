using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
using RuleLoom.Schemas;
using RuleLoom.Validation;
using static RuleLoom.Extensions.DependencyInjection.Tests.AddRuleLoomTests;

namespace RuleLoom.Extensions.DependencyInjection.Tests;

public class CatalogStoreTests
{
    [Fact]
    public async Task UseStore_Instance_BuildsFromCodeAndTheStore_AndSavesTheResult()
    {
        var store = new InMemoryCatalogStore();
        await store.SaveAsync(new CatalogDocument([EventSchemaDefinition.Parse("""{ "x-ruleloom-event": "AccountClosed", "type": "object" }""")]),
            TestContext.Current.CancellationToken);
        var services = new ServiceCollection()
            .AddSingleton<Accounts>()
            .AddRuleLoom(r => r.AddCatalog(c => c.AddEvent<TransferMadeSchema>().AddContextProvider<AccountProvider>("account")).UseStore(store));

        await using var provider = await StartAsync(services);
        using var scope = provider.CreateScope();

        Assert.Equal(["TransferMade", "AccountClosed"], scope.ServiceProvider.GetRequiredService<Catalog>().Events.Select(e => e.EventType));
        var saved = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["AccountClosed", "TransferMade"], saved.Events.Select(e => e.EventType));
        Assert.Equal("Account", Assert.Single(saved.Contexts).Label);
    }

    [Fact]
    public async Task UseStore_Type_IsScopedAndUsedAtStartup()
    {
        var services = new ServiceCollection()
            .AddSingleton<SavedDocuments>()
            .AddRuleLoom(r => r.AddCatalog(c => c.AddAction<AuditAction>()).UseStore<RecordingStore>());

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(ICatalogStore)).Lifetime);

        await using var provider = await StartAsync(services);

        var saved = Assert.Single(provider.GetRequiredService<SavedDocuments>().Documents);
        Assert.Equal(["audit"], saved.Actions.Select(a => a.Name));
    }

    [Fact]
    public async Task RuleEngine_LoadsBothContextsWithOneProvider()
    {
        var services = new ServiceCollection()
            .AddSingleton<Accounts>()
            .AddRuleLoom(r => r.AddCatalog(c => c.AddEvent<TransferMadeSchema>().AddContextProvider<AccountProvider>("account")));

        await using var provider = await StartAsync(services);
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

    [Fact]
    public async Task RuleValidator_ChecksPathsAgainstTheEventAndProviderSchemas()
    {
        var services = new ServiceCollection()
            .AddSingleton<Accounts>()
            .AddRuleLoom(r => r.AddCatalog(c => c.AddEvent<TransferMadeSchema>().AddContextProvider<AccountProvider>("account")));

        await using var provider = await StartAsync(services);
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<RuleValidator>();
        var rule = RuleJsonParser.Parse("""
            { "id": "r", "name": "R", "eventType": "TransferMade",
              "condition": { "operator": "and", "operands": [
                { "operator": "eq", "operands": [{ "path": "@toAccount.frozen" }, { "value": true }] },
                { "operator": "eq", "operands": [{ "path": "@toAccount.frozn" }, { "value": true }] } ] } }
            """);

        var result = validator.Validate(rule);

        Assert.Equal("$.condition.operands[1].operands[0].path", Assert.Single(result.Errors).Path);
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

    public sealed record AccountDto(string Id, bool Frozen);

    public sealed class AccountSchema : ContextSchema<AccountDto>
    {
        public AccountSchema() => Label("Account");
    }

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

    public sealed class SavedDocuments
    {
        public List<CatalogDocument> Documents { get; } = [];
    }

    public sealed class RecordingStore(SavedDocuments saved) : ICatalogStore
    {
        public ValueTask<CatalogDocument> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new CatalogDocument());

        public ValueTask SaveAsync(CatalogDocument document, CancellationToken cancellationToken = default)
        {
            saved.Documents.Add(document);
            return ValueTask.CompletedTask;
        }
    }
}

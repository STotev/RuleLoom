using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
using RuleLoom.Schemas;
using RuleLoom.Validation;

namespace RuleLoom.Extensions.DependencyInjection.Tests;

public class AddRuleLoomTests
{
    private const string Json = """
    {
      "id": "welcome",
      "name": "Welcome",
      "eventType": "UserCreated",
      "condition": { "operator": "eq", "operands": [{ "path": "Tier" }, { "value": "vip" }] },
      "actions": [{ "action": "send-email", "parameters": { "template": "vip-welcome" } }]
    }
    """;

    [Fact]
    public void AddRuleLoom_RegistersServicesWithExpectedLifetimes()
    {
        var services = new ServiceCollection().AddRuleLoom();

        Assert.Equal(ServiceLifetime.Singleton, Lifetime<TimeProvider>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<Catalog>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<IActionFactory>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<IContextProviderFactory>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<RuleEngine>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<RuleValidator>(services));
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task AddRuleLoom_CalledTwice_ConfiguresOneCatalog()
    {
        var services = new ServiceCollection()
            .AddRuleLoom(rules => rules.AddCatalog(c => c.AddAction<SendEmailAction>()))
            .AddRuleLoom(rules => rules.AddCatalog(c => c.AddAction<AuditAction>()).AddCatalog(c => c.AddAction<SendEmailAction>()));

        Assert.Single(services, d => d.ServiceType == typeof(RuleEngine));
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));

        await using var provider = await StartAsync(services);
        using var scope = provider.CreateScope();

        Assert.Equal(["send-email", "audit"], scope.ServiceProvider.GetRequiredService<Catalog>().Actions.Select(a => a.Name));
    }

    [Fact]
    public void AddRuleLoom_ExistingTimeProvider_IsKept()
    {
        var fixedTime = new FixedTimeProvider(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection().AddSingleton<TimeProvider>(fixedTime).AddRuleLoom();

        using var provider = Build(services);

        Assert.Same(fixedTime, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Catalog_BeforeStartup_Throws()
    {
        using var provider = Build(new ServiceCollection().AddRuleLoom());
        using var scope = provider.CreateScope();

        var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<RuleEngine>());

        Assert.StartsWith("RuleLoom hasn't started", error.Message);
    }

    [Fact]
    public async Task HostStart_BuildsTheCatalog_AndEveryScopeSharesIt()
    {
        await using var provider = Build(new ServiceCollection().AddRuleLoom(rules => rules.AddCatalog(c => c.AddEvent<UserCreatedSchema>()
            .AddContextProvider<CustomerProvider>("customer"))));

        foreach (var hostedService in provider.GetServices<IHostedService>())
            await hostedService.StartAsync(TestContext.Current.CancellationToken);
        await provider.StartRuleLoomAsync(TestContext.Current.CancellationToken);

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        Assert.Same(first.ServiceProvider.GetRequiredService<Catalog>(), second.ServiceProvider.GetRequiredService<Catalog>());
        Assert.Equal(["UserCreated"], first.ServiceProvider.GetRequiredService<Catalog>().Events.Select(e => e.EventType));
    }

    [Fact]
    public async Task HostStart_BrokenCatalog_Throws()
    {
        await using var provider = Build(new ServiceCollection().AddRuleLoom(rules => rules.AddCatalog(c => c.AddEvent<UserCreatedSchema>())));
        var startup = Assert.Single(provider.GetServices<IHostedService>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => startup.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("No context provider is registered under 'customer'", error.Message);
    }

    [Fact]
    public async Task StartRuleLoomAsync_WithoutAddRuleLoom_Throws()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StartRuleLoomAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RuleEngine_FromScope_RunsActionsAndContextsWithTheirDependencies()
    {
        var outbox = new Outbox();
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender>(outbox)
            .AddRuleLoom(rules => rules.AddCatalog(c => c
                .AddEvent<UserCreatedSchema>()
                .AddAction<SendEmailAction>()
                .AddContextProvider<CustomerProvider>("customer")));

        await using var provider = await StartAsync(services);
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
        var rule = RuleJsonParser.Parse(Json.Replace("\"path\": \"Tier\"", "\"path\": \"@customer.Tier\""));

        var result = await engine.ExecuteAsync("UserCreated", new UserDto("u-1", "c-1", "basic"), [rule], TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.Equal(["vip-welcome"], outbox.Sent);
    }

    [Fact]
    public async Task Factories_UseRegisteredServices_AndCreateTheRestPerCall()
    {
        var audit = new AuditAction();
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddSingleton(audit)
            .AddRuleLoom(rules => rules.AddCatalog(c => c.AddAction<AuditAction>().AddAction<SendEmailAction>()));

        await using var provider = await StartAsync(services);
        using var scope = provider.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<Catalog>();
        var factory = scope.ServiceProvider.GetRequiredService<IActionFactory>();
        catalog.TryGetAction("audit", out var auditRegistration);
        catalog.TryGetAction("send-email", out var sendEmailRegistration);

        Assert.Same(audit, factory.Create(auditRegistration!));
        Assert.NotSame(factory.Create(sendEmailRegistration!), factory.Create(sendEmailRegistration!));
    }

    [Fact]
    public async Task RuleValidator_FromScope_ChecksAgainstTheCatalog()
    {
        var services = new ServiceCollection()
            .AddRuleLoom(rules => rules.AddCatalog(c => c
                .AddEvent<UserCreatedSchema>()
                .AddAction<SendEmailAction>()
                .AddContextProvider<CustomerProvider>("customer")));

        await using var provider = await StartAsync(services);
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<RuleValidator>();

        Assert.True(validator.Validate(RuleJsonParser.Parse(Json)).IsValid);

        var unknown = RuleJsonParser.Parse(Json.Replace("\"send-email\"", "\"send-sms\"").Replace("\"eq\"", "\"contains\""));
        Assert.Equal(["$.condition.operator", "$.actions[0].action"], validator.Validate(unknown).Errors.Select(e => e.Path));
    }

    internal static async Task<ServiceProvider> StartAsync(IServiceCollection services)
    {
        var provider = Build(services);
        await provider.StartRuleLoomAsync(TestContext.Current.CancellationToken);
        return provider;
    }

    internal static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static ServiceLifetime Lifetime<T>(IServiceCollection services) =>
        Assert.Single(services, d => d.ServiceType == typeof(T)).Lifetime;
}

public interface IEmailSender
{
    void Send(string template);
}

public sealed class Outbox : IEmailSender
{
    public List<string> Sent { get; } = [];
    public void Send(string template) => Sent.Add(template);
}

public sealed class SendEmailAction(IEmailSender sender) : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        sender.Send(action.GetParameter<string>("template"));
        return Task.FromResult(ActionResult.Success());
    }
}

public sealed record UserDto(string Id, string CustomerId, string Tier);

public sealed class UserCreatedSchema : EventSchema<UserDto>
{
    public UserCreatedSchema()
    {
        Event("UserCreated");
        Context("customer").ReferencedBy(u => u.CustomerId);
    }
}

public sealed class CustomerProvider(TimeProvider clock) : IContextProvider
{
    public ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<object?>(new { Id = request.GetReference<string>(), Tier = "vip", LoadedAt = clock.GetUtcNow() });
}

public sealed class AuditAction : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActionResult.Success());
}

public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
using RuleLoom.Extensions.DependencyInjection;
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
    public void AddRuleLoom_RegistersCoreServicesWithExpectedLifetimes()
    {
        var services = new ServiceCollection().AddRuleLoom(rules => rules.AddAction<SendEmailAction>());

        Assert.Equal(ServiceLifetime.Singleton, Lifetime<IConditionEvaluator>(services));
        Assert.Equal(ServiceLifetime.Singleton, Lifetime<TimeProvider>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<IActionFactory>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<RuleEngine>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<RuleValidator>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<SendEmailAction>(services));
    }

    [Fact]
    public void AddRuleLoom_CalledTwice_AddsCoreServicesOnceAndKeepsAllActions()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleLoom(rules => rules.AddAction<SendEmailAction>())
            .AddRuleLoom(rules => rules.AddAction<AuditAction>());

        Assert.Single(services, d => d.ServiceType == typeof(RuleEngine));
        Assert.Single(services, d => d.ServiceType == typeof(IConditionEvaluator));

        using var provider = Build(services);
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IActionFactory>();

        Assert.Equal(["audit", "send-email"], factory.ActionNames.Order());
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
    public async Task RuleEngine_FromScope_RunsRuleWithActionThatHasDependencies()
    {
        var outbox = new Outbox();
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender>(outbox)
            .AddRuleLoom(rules => rules.AddAction<SendEmailAction>());

        using var provider = Build(services);
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();

        var result = await engine.ExecuteAsync(
            "UserCreated", new { Tier = "vip" }, [RuleJsonParser.Parse(Json)], TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.Equal(["vip-welcome"], outbox.Sent);
    }

    [Fact]
    public async Task RuleEngine_FromScope_LoadsContextsFromScopedProviders()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleLoom(rules => rules
                .AddAction<SendEmailAction>()
                .AddContextProvider<CustomerProvider>("customer"));

        using var provider = Build(services);
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
        var rule = RuleJsonParser.Parse(Json.Replace("\"path\": \"Tier\"", "\"path\": \"@customer.Tier\""));

        var result = await engine.ExecuteAsync("UserCreated", new { CustomerId = "c-1" }, [rule], TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
    }

    [Fact]
    public void ContextProviders_AreScopedAndNamesIgnoreCase()
    {
        var services = new ServiceCollection().AddRuleLoom(rules => rules.AddContextProvider<CustomerProvider>("customer"));

        using var provider = Build(services);
        IContextProvider? first, again, other;

        using (var scope = provider.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IContextProviderFactory>();
            factory.TryGetProvider("customer", out first);
            factory.TryGetProvider("CUSTOMER", out again);
            Assert.Equal(["customer"], factory.ProviderNames);
        }

        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<IContextProviderFactory>().TryGetProvider("customer", out other);

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.NotSame(first, other);
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<CustomerProvider>(services));
    }

    [Fact]
    public void AddContextProvider_NameTakenByAnotherType_Throws()
    {
        var services = new ServiceCollection();

        services.AddRuleLoom(rules => rules.AddContextProvider<CustomerProvider>("customer"));

        Assert.Throws<ArgumentException>(() => services.AddRuleLoom(rules => rules.AddContextProvider<OtherProvider>("Customer")));
    }

    [Fact]
    public void Actions_AreScoped_NewInstancePerScope()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleLoom(rules => rules.AddAction<SendEmailAction>());

        using var provider = Build(services);
        IAction? first, again, other;

        using (var scope = provider.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IActionFactory>();
            factory.TryGetAction("send-email", out first);
            factory.TryGetAction("SEND-EMAIL", out again);
        }

        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<IActionFactory>().TryGetAction("send-email", out other);

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.NotSame(first, other);
    }

    [Fact]
    public void AddAction_WithName_RegistersUnderThatNameAndTypeCanHaveSeveralNames()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleLoom(rules => rules.AddAction<SendEmailAction>().AddAction<SendEmailAction>("notify"));

        using var provider = Build(services);
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IActionFactory>();

        Assert.Equal(["notify", "send-email"], factory.ActionNames.Order());
        Assert.True(factory.TryGetAction("notify", out var executor));
        Assert.IsType<SendEmailAction>(executor);
        Assert.False(factory.TryGetAction("unknown", out _));
    }

    [Fact]
    public void AddAction_SameTypeAndNameTwice_IsRegisteredOnce()
    {
        var services = new ServiceCollection().AddRuleLoom(rules => rules.AddAction<AuditAction>().AddAction<AuditAction>());

        Assert.Single(services, d => d.ServiceType == typeof(AuditAction));
    }

    [Fact]
    public void AddAction_NameTakenByAnotherType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRuleLoom(rules =>
            rules.AddAction<SendEmailAction>().AddAction<AuditAction>("send-email")));

        Assert.StartsWith("Action name 'send-email' is already registered for SendEmailAction; can't register AuditAction under it.", ex.Message);
    }

    [Fact]
    public void AddAction_NameDifferingOnlyInCase_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRuleLoom(rules =>
            rules.AddAction<AuditAction>().AddAction<SendEmailAction>("AUDIT")));

        Assert.StartsWith("Action name 'AUDIT' conflicts with 'audit' (names are case-insensitive).", ex.Message);
    }

    [Fact]
    public void RuleValidator_FromScope_KnowsRegisteredActionsAndOperators()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleLoom(rules => rules.AddAction<SendEmailAction>());

        using var provider = Build(services);
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<RuleValidator>();

        Assert.True(validator.Validate(RuleJsonParser.Parse(Json)).IsValid);

        var unknown = RuleJsonParser.Parse(Json.Replace("\"send-email\"", "\"send-sms\"").Replace("\"eq\"", "\"contains\""));
        Assert.Equal(["$.condition.operator", "$.actions[0].action"], validator.Validate(unknown).Errors.Select(e => e.Path));
    }

    private static ServiceProvider Build(IServiceCollection services) =>
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

public sealed class CustomerProvider : IContextProvider
{
    public ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<object?>(new { Id = request.Resolve("CustomerId"), Tier = "vip" });
}

public sealed class OtherProvider : IContextProvider
{
    public ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<object?>(null);
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

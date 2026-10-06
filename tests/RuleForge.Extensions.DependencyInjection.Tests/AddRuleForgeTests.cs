using Microsoft.Extensions.DependencyInjection;
using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Engine;
using RuleForge.Extensions.DependencyInjection;
using RuleForge.Validation;

namespace RuleForge.Extensions.DependencyInjection.Tests;

public class AddRuleForgeTests
{
    private const string Json = """
    {
      "name": "Welcome",
      "eventType": "UserCreated",
      "condition": { "operator": "eq", "operands": [{ "path": "Tier" }, { "value": "vip" }] },
      "actions": [{ "action": "send-email", "parameters": { "template": "vip-welcome" } }]
    }
    """;

    [Fact]
    public void AddRuleForge_RegistersCoreServicesWithExpectedLifetimes()
    {
        var services = new ServiceCollection().AddRuleForge(rules => rules.AddAction<SendEmailAction>());

        Assert.Equal(ServiceLifetime.Singleton, Lifetime<IConditionEvaluator>(services));
        Assert.Equal(ServiceLifetime.Singleton, Lifetime<TimeProvider>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<IActionExecutorFactory>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<RuleEngine>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<RuleValidator>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<SendEmailAction>(services));
    }

    [Fact]
    public void AddRuleForge_CalledTwice_AddsCoreServicesOnceAndKeepsAllActions()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleForge(rules => rules.AddAction<SendEmailAction>())
            .AddRuleForge(rules => rules.AddAction<AuditAction>());

        Assert.Single(services, d => d.ServiceType == typeof(RuleEngine));
        Assert.Single(services, d => d.ServiceType == typeof(IConditionEvaluator));

        using var provider = Build(services);
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IActionExecutorFactory>();

        Assert.Equal(["audit", "send-email"], factory.ActionNames.Order());
    }

    [Fact]
    public void AddRuleForge_ExistingTimeProvider_IsKept()
    {
        var fixedTime = new FixedTimeProvider(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection().AddSingleton<TimeProvider>(fixedTime).AddRuleForge();

        using var provider = Build(services);

        Assert.Same(fixedTime, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task RuleEngine_FromScope_RunsRuleWithActionThatHasDependencies()
    {
        var outbox = new Outbox();
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender>(outbox)
            .AddRuleForge(rules => rules.AddAction<SendEmailAction>());

        using var provider = Build(services);
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();

        var result = await engine.ExecuteAsync(
            "UserCreated", new { Tier = "vip" }, [RuleJsonParser.Parse(Json)], TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.Equal(["vip-welcome"], outbox.Sent);
    }

    [Fact]
    public void Actions_AreScoped_NewInstancePerScope()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleForge(rules => rules.AddAction<SendEmailAction>());

        using var provider = Build(services);
        IActionExecutor? first, again, other;

        using (var scope = provider.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IActionExecutorFactory>();
            factory.TryGetExecutor("send-email", out first);
            factory.TryGetExecutor("SEND-EMAIL", out again);
        }

        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<IActionExecutorFactory>().TryGetExecutor("send-email", out other);

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.NotSame(first, other);
    }

    [Fact]
    public void AddAction_WithName_RegistersUnderThatNameAndTypeCanHaveSeveralNames()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleForge(rules => rules.AddAction<SendEmailAction>().AddAction<SendEmailAction>("notify"));

        using var provider = Build(services);
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IActionExecutorFactory>();

        Assert.Equal(["notify", "send-email"], factory.ActionNames.Order());
        Assert.True(factory.TryGetExecutor("notify", out var executor));
        Assert.IsType<SendEmailAction>(executor);
        Assert.False(factory.TryGetExecutor("unknown", out _));
    }

    [Fact]
    public void AddAction_SameTypeAndNameTwice_IsRegisteredOnce()
    {
        var services = new ServiceCollection().AddRuleForge(rules => rules.AddAction<AuditAction>().AddAction<AuditAction>());

        Assert.Single(services, d => d.ServiceType == typeof(AuditAction));
    }

    [Fact]
    public void AddAction_NameTakenByAnotherType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRuleForge(rules =>
            rules.AddAction<SendEmailAction>().AddAction<AuditAction>("send-email")));

        Assert.StartsWith("Action name 'send-email' is already registered for SendEmailAction; can't register AuditAction under it.", ex.Message);
    }

    [Fact]
    public void AddAction_NameDifferingOnlyInCase_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRuleForge(rules =>
            rules.AddAction<AuditAction>().AddAction<SendEmailAction>("AUDIT")));

        Assert.StartsWith("Action name 'AUDIT' conflicts with 'audit' (names are case-insensitive).", ex.Message);
    }

    [Fact]
    public void RuleValidator_FromScope_KnowsRegisteredActionsAndOperators()
    {
        var services = new ServiceCollection()
            .AddSingleton<IEmailSender, Outbox>()
            .AddRuleForge(rules => rules.AddAction<SendEmailAction>());

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

public sealed class SendEmailAction(IEmailSender sender) : IActionExecutor
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        sender.Send(action.GetParameter<string>("template"));
        return Task.FromResult(ActionResult.Success());
    }
}

public sealed class AuditAction : IActionExecutor
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActionResult.Success());
}

public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

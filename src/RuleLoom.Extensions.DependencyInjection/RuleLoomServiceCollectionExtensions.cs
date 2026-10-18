using Microsoft.Extensions.DependencyInjection.Extensions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Engine;
using RuleLoom.Evaluators;
using RuleLoom.Extensions.DependencyInjection;
using RuleLoom.Validation;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers RuleLoom with a service collection.
/// </summary>
public static class RuleLoomServiceCollectionExtensions
{
    /// <summary>
    /// Registers the rule engine, its evaluator (with the built-in operators), the action factory and the rule
    /// validator, and applies <paramref name="configure"/>. Safe to call more than once: core services are added once,
    /// and each call adds its actions and operators.
    /// </summary>
    /// <remarks>
    /// Lifetimes: operators and <see cref="IConditionEvaluator"/> are Singleton; actions, context providers,
    /// <see cref="IActionFactory"/>, <see cref="IContextProviderFactory"/>, <see cref="RuleEngine"/> and
    /// <see cref="RuleValidator"/> are Scoped.
    /// <see cref="TimeProvider.System"/> is registered if no <see cref="TimeProvider"/> is.
    /// </remarks>
    public static IServiceCollection AddRuleLoom(this IServiceCollection services, Action<RuleLoomBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = GetOrAddRegistry(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new RuleLoomOperators(registry.BuildOperators(sp)));
        services.TryAddSingleton<IConditionEvaluator>(sp => new ConditionEvaluator(sp.GetRequiredService<RuleLoomOperators>().All));
        services.TryAddScoped<IActionFactory>(sp => new ServiceProviderActionFactory(sp, registry));
        services.TryAddScoped<IContextProviderFactory>(sp => new ServiceProviderContextProviderFactory(sp, registry));
        services.TryAddScoped(sp => new RuleEngine(
            sp.GetRequiredService<IConditionEvaluator>(),
            sp.GetRequiredService<IActionFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IContextProviderFactory>(),
            sp.GetService<RuleLoom.Schemas.ISchemaStore>()));
        services.TryAddScoped(sp => new RuleValidator(
            sp.GetRequiredService<RuleLoomOperators>().All,
            sp.GetRequiredService<IActionFactory>()));

        configure?.Invoke(new RuleLoomBuilder(services, registry));

        return services;
    }

    private static RuleLoomRegistry GetOrAddRegistry(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(RuleLoomRegistry))?.ImplementationInstance is RuleLoomRegistry existing)
            return existing;

        var registry = new RuleLoomRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}

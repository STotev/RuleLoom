using Microsoft.Extensions.DependencyInjection.Extensions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Engine;
using RuleForge.Evaluators;
using RuleForge.Extensions.DependencyInjection;
using RuleForge.Validation;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers RuleForge with a service collection.
/// </summary>
public static class RuleForgeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the rule engine, its evaluator (with the built-in operators), the action factory and the rule
    /// validator, and applies <paramref name="configure"/>. Safe to call more than once: core services are added once,
    /// and each call adds its actions and operators.
    /// </summary>
    /// <remarks>
    /// Lifetimes: operators and <see cref="IConditionEvaluator"/> are Singleton; actions,
    /// <see cref="IActionExecutorFactory"/>, <see cref="RuleEngine"/> and <see cref="RuleValidator"/> are Scoped.
    /// <see cref="TimeProvider.System"/> is registered if no <see cref="TimeProvider"/> is.
    /// </remarks>
    public static IServiceCollection AddRuleForge(this IServiceCollection services, Action<RuleForgeBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = GetOrAddRegistry(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new RuleForgeOperators(registry.BuildOperators(sp)));
        services.TryAddSingleton<IConditionEvaluator>(sp => new ConditionEvaluator(sp.GetRequiredService<RuleForgeOperators>().All));
        services.TryAddScoped<IActionExecutorFactory>(sp => new ServiceProviderActionExecutorFactory(sp, registry));
        services.TryAddScoped(sp => new RuleEngine(
            sp.GetRequiredService<IConditionEvaluator>(),
            sp.GetRequiredService<IActionExecutorFactory>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddScoped(sp => new RuleValidator(
            sp.GetRequiredService<RuleForgeOperators>().All,
            sp.GetRequiredService<IActionExecutorFactory>()));

        configure?.Invoke(new RuleForgeBuilder(services, registry));

        return services;
    }

    private static RuleForgeRegistry GetOrAddRegistry(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(RuleForgeRegistry))?.ImplementationInstance is RuleForgeRegistry existing)
            return existing;

        var registry = new RuleForgeRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}

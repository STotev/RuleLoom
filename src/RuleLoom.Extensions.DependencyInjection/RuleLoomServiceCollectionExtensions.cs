using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Context;
using RuleLoom.Engine;
using RuleLoom.Extensions.DependencyInjection;
using RuleLoom.Validation;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers RuleLoom with a service collection.
/// </summary>
public static class RuleLoomServiceCollectionExtensions
{
    /// <summary>
    /// Registers the rule engine, the rule validator and the factories they create actions and context providers with,
    /// and applies <paramref name="configure"/>. Safe to call more than once: every call configures the same catalog.
    /// The catalog is built when the host starts (or by <c>StartRuleLoomAsync()</c> in apps without a host); a broken
    /// catalog stops the app from starting.
    /// </summary>
    /// <remarks>
    /// Lifetimes: <see cref="Catalog"/>, <see cref="IActionFactory"/>, <see cref="IContextProviderFactory"/>,
    /// <see cref="RuleEngine"/> and <see cref="RuleValidator"/> are Scoped. Actions and context providers are created in
    /// the scope: as registered when their type is registered, otherwise with their dependencies from the scope.
    /// <see cref="TimeProvider.System"/> is registered if no <see cref="TimeProvider"/> is.
    /// </remarks>
    public static IServiceCollection AddRuleLoom(this IServiceCollection services, Action<RuleLoomBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var holder = GetOrAddHolder(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CatalogStartup>());
        services.TryAddScoped(sp => sp.GetRequiredService<CatalogHolder>().Current ?? throw new InvalidOperationException(
            "RuleLoom hasn't started: the catalog is built when the host starts, or by StartRuleLoomAsync() in apps without a host."));
        services.TryAddScoped<IActionFactory>(sp => new ServiceProviderActionFactory(sp));
        services.TryAddScoped<IContextProviderFactory>(sp => new ServiceProviderContextProviderFactory(sp));
        services.TryAddScoped(sp => new RuleEngine(
            sp.GetRequiredService<Catalog>(),
            sp.GetRequiredService<IActionFactory>(),
            sp.GetRequiredService<IContextProviderFactory>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddScoped(sp => new RuleValidator(sp.GetRequiredService<Catalog>()));

        configure?.Invoke(new RuleLoomBuilder(services, holder.Builder));

        return services;
    }

    private static CatalogHolder GetOrAddHolder(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(CatalogHolder))?.ImplementationInstance is CatalogHolder existing)
            return existing;

        var holder = new CatalogHolder();
        services.AddSingleton(holder);
        return holder;
    }
}

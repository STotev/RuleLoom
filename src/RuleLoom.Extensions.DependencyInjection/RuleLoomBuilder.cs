using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuleLoom.Engine;
using RuleLoom.Schemas;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// Configures RuleLoom for <c>AddRuleLoom</c>: the catalog (events, context providers, operators, actions) and where it
/// is kept.
/// </summary>
public sealed class RuleLoomBuilder
{
    private readonly CatalogBuilder _catalog;
    private readonly RuleEngineOptions _engineOptions;

    internal RuleLoomBuilder(IServiceCollection services, CatalogBuilder catalog, RuleEngineOptions engineOptions)
    {
        Services = services;
        _catalog = catalog;
        _engineOptions = engineOptions;
    }

    /// <summary>
    /// The service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Configures the catalog. Every call (also from other <c>AddRuleLoom</c> calls) configures the same builder; the
    /// catalog is built once, at startup.
    /// </summary>
    public RuleLoomBuilder AddCatalog(Action<CatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_catalog);
        return this;
    }

    /// <summary>
    /// Configures how the engine runs rules, e.g. skipping overdue actions. Every call (also from other
    /// <c>AddRuleLoom</c> calls) configures the same options; engines read them when they're created.
    /// </summary>
    public RuleLoomBuilder Configure(Action<RuleEngineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_engineOptions);
        return this;
    }

    /// <summary>
    /// Keeps the catalog in <typeparamref name="TStore"/> (Scoped, e.g. over a <c>DbContext</c>): it is built from code
    /// plus what the store holds, and saved back, at startup.
    /// </summary>
    public RuleLoomBuilder UseStore<TStore>() where TStore : class, ICatalogStore
    {
        Services.Replace(ServiceDescriptor.Scoped<ICatalogStore, TStore>());
        return this;
    }

    /// <summary>
    /// Keeps the catalog in <paramref name="store"/> (e.g. an <see cref="InMemoryCatalogStore"/>, which must be one
    /// instance to keep its contents).
    /// </summary>
    public RuleLoomBuilder UseStore(ICatalogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        Services.Replace(ServiceDescriptor.Singleton(store));
        return this;
    }
}

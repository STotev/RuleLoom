using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuleLoom.Schemas;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// Configures RuleLoom for <c>AddRuleLoom</c>: the catalog (events, context providers, operators, actions) and where it
/// is kept.
/// </summary>
public sealed class RuleLoomBuilder
{
    private readonly CatalogBuilder _catalog;

    internal RuleLoomBuilder(IServiceCollection services, CatalogBuilder catalog)
    {
        Services = services;
        _catalog = catalog;
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

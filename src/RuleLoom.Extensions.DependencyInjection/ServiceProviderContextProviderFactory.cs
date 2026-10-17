using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions.Context;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// Resolves context providers registered with <see cref="RuleLoomBuilder.AddContextProvider{TProvider}"/> from the
/// current scope, so providers can use scoped services.
/// </summary>
public sealed class ServiceProviderContextProviderFactory : IContextProviderFactory
{
    private readonly IServiceProvider _services;
    private readonly RuleLoomRegistry _registry;

    internal ServiceProviderContextProviderFactory(IServiceProvider services, RuleLoomRegistry registry)
    {
        _services = services;
        _registry = registry;
    }

    public IReadOnlyCollection<string> ProviderNames => _registry.ContextProviderNames;

    public bool TryGetProvider(string name, out IContextProvider? provider)
    {
        provider = _registry.TryGetContextProviderType(name, out var providerType)
            ? (IContextProvider)_services.GetRequiredService(providerType!)
            : null;

        return provider is not null;
    }
}

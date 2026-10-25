using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions.Context;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// Creates context providers in the current scope: the registered service when the provider's type is registered,
/// otherwise a new instance with its dependencies from the scope. Instances that aren't registered aren't disposed with
/// the scope, so register disposable providers yourself.
/// </summary>
public sealed class ServiceProviderContextProviderFactory : IContextProviderFactory
{
    private readonly IServiceProvider _services;

    internal ServiceProviderContextProviderFactory(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public IContextProvider Create(ContextProviderRegistration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return (IContextProvider)ActivatorUtilities.GetServiceOrCreateInstance(_services, provider.Type);
    }
}

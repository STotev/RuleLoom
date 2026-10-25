using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions.Action;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// Creates actions in the current scope: the registered service when the action's type is registered, otherwise a new
/// instance with its dependencies from the scope. Instances that aren't registered aren't disposed with the scope, so
/// register disposable actions yourself.
/// </summary>
public sealed class ServiceProviderActionFactory : IActionFactory
{
    private readonly IServiceProvider _services;

    internal ServiceProviderActionFactory(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public IAction Create(ActionRegistration action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (IAction)ActivatorUtilities.GetServiceOrCreateInstance(_services, action.Type);
    }
}

using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions.Action;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// Resolves actions registered with <see cref="RuleLoomBuilder.AddAction{TAction}"/> from the current scope, so
/// each scope gets its own action instances and actions can use scoped services.
/// </summary>
public sealed class ServiceProviderActionFactory : IActionFactory
{
    private readonly IServiceProvider _services;
    private readonly RuleLoomRegistry _registry;

    internal ServiceProviderActionFactory(IServiceProvider services, RuleLoomRegistry registry)
    {
        _services = services;
        _registry = registry;
    }

    public IReadOnlyCollection<string> ActionNames => _registry.ActionNames;

    public bool TryGetAction(string name, out IAction? action)
    {
        action = _registry.TryGetActionType(name, out var actionType)
            ? (IAction)_services.GetRequiredService(actionType!)
            : null;

        return action is not null;
    }
}

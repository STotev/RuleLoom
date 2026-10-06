using Microsoft.Extensions.DependencyInjection;
using RuleForge.Abstractions.Action;

namespace RuleForge.Extensions.DependencyInjection;

/// <summary>
/// Resolves actions registered with <see cref="RuleForgeBuilder.AddAction{TAction}"/> from the current scope, so
/// each scope gets its own action instances and actions can use scoped services.
/// </summary>
public sealed class ServiceProviderActionFactory : IActionFactory
{
    private readonly IServiceProvider _services;
    private readonly RuleForgeRegistry _registry;

    internal ServiceProviderActionFactory(IServiceProvider services, RuleForgeRegistry registry)
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

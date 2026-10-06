using Microsoft.Extensions.DependencyInjection;
using RuleForge.Abstractions.Action;

namespace RuleForge.Extensions.DependencyInjection;

/// <summary>
/// Resolves actions registered with <see cref="RuleForgeBuilder.AddAction{TAction}"/> from the current scope, so
/// each scope gets its own action instances and actions can use scoped services.
/// </summary>
public sealed class ServiceProviderActionExecutorFactory : IActionExecutorFactory
{
    private readonly IServiceProvider _services;
    private readonly RuleForgeRegistry _registry;

    internal ServiceProviderActionExecutorFactory(IServiceProvider services, RuleForgeRegistry registry)
    {
        _services = services;
        _registry = registry;
    }

    public IReadOnlyCollection<string> ActionNames => _registry.ActionNames;

    public bool TryGetExecutor(string name, out IActionExecutor? executor)
    {
        executor = _registry.TryGetActionType(name, out var actionType)
            ? (IActionExecutor)_services.GetRequiredService(actionType!)
            : null;

        return executor is not null;
    }
}

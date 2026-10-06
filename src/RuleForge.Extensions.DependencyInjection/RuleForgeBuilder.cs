using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;

namespace RuleForge.Extensions.DependencyInjection;

/// <summary>
/// Registers actions and operators for <c>AddRuleForge</c>. The built-in operators are registered automatically;
/// operator additions and removals apply in call order.
/// </summary>
public sealed class RuleForgeBuilder
{
    private readonly RuleForgeRegistry _registry;

    internal RuleForgeBuilder(IServiceCollection services, RuleForgeRegistry registry)
    {
        Services = services;
        _registry = registry;
    }

    /// <summary>
    /// The service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers <typeparamref name="TAction"/> (Scoped) under <paramref name="name"/>, or under its default name
    /// (<see cref="Abstractions.Action.ActionNames.GetDefaultName"/>) when omitted. The same type may be registered
    /// under several names.
    /// </summary>
    /// <exception cref="ArgumentException">The name is already used by another action (names are case-insensitive).</exception>
    public RuleForgeBuilder AddAction<TAction>(string? name = null) where TAction : class, IActionExecutor
    {
        _registry.AddAction(name ?? Abstractions.Action.ActionNames.GetDefaultName(typeof(TAction)), typeof(TAction));
        Services.TryAddScoped<TAction>();
        return this;
    }

    /// <summary>
    /// Adds <typeparamref name="TOperator"/> (Singleton). Its name must not clash with an operator that is still
    /// registered: to swap out an operator, remove it first (<c>RemoveOperator("eq").AddOperator&lt;MyEquals&gt;()</c>).
    /// </summary>
    public RuleForgeBuilder AddOperator<TOperator>() where TOperator : class, IConditionOperator
    {
        _registry.AddOperator(typeof(TOperator));
        return this;
    }

    /// <summary>
    /// Removes the operators registered so far (built-in or added) whose name matches, ignoring case.
    /// </summary>
    public RuleForgeBuilder RemoveOperator(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _registry.RemoveOperator(name);
        return this;
    }

    /// <summary>
    /// Removes all built-in operators, keeping operators added so far. Use it to start from a fully custom set.
    /// </summary>
    public RuleForgeBuilder RemoveBuiltInOperators()
    {
        _registry.RemoveBuiltInOperators();
        return this;
    }
}

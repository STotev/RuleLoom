using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Schemas;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// Registers actions, operators and context providers for <c>AddRuleLoom</c>. The built-in operators are registered automatically;
/// operator additions and removals apply in call order.
/// </summary>
public sealed class RuleLoomBuilder
{
    private readonly RuleLoomRegistry _registry;

    internal RuleLoomBuilder(IServiceCollection services, RuleLoomRegistry registry)
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
    public RuleLoomBuilder AddAction<TAction>(string? name = null) where TAction : class, IAction
    {
        _registry.AddAction(name ?? Abstractions.Action.ActionNames.GetDefaultName(typeof(TAction)), typeof(TAction));
        Services.TryAddScoped<TAction>();
        return this;
    }

    /// <summary>
    /// Registers <typeparamref name="TProvider"/> (Scoped) as the provider of the context <paramref name="name"/>,
    /// which rules use as <c>@name</c>. The same type may provide several contexts.
    /// </summary>
    /// <exception cref="ArgumentException">The name is already used by another provider (names are case-insensitive).</exception>
    public RuleLoomBuilder AddContextProvider<TProvider>(string name) where TProvider : class, IContextProvider
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _registry.AddContextProvider(name, typeof(TProvider));
        Services.TryAddScoped<TProvider>();
        return this;
    }

    /// <summary>
    /// Registers event schemas: an <see cref="ISchemaStore"/> (the in-memory store seeded with the schemas configured here,
    /// Singleton, or your own with <see cref="SchemaRegistration.UseStore{TStore}"/>, Scoped). The engine reads it, so
    /// context providers receive each event's declared reference. Call it once; a later call replaces the store.
    /// </summary>
    /// <exception cref="InvalidOperationException">Schemas were added together with your own store.</exception>
    public RuleLoomBuilder AddSchemas(Action<SchemaRegistration>? configure = null)
    {
        var registration = new SchemaRegistration();
        configure?.Invoke(registration);

        if (registration.StoreType is { } storeType)
        {
            if (registration.HasSeeds)
                throw new InvalidOperationException($"Schemas can't be added together with {storeType.Name}; save them to your store instead.");

            Services.Replace(ServiceDescriptor.Scoped(typeof(ISchemaStore), storeType));
        }
        else
        {
            var seeds = registration.BuildSeeds();
            Services.Replace(ServiceDescriptor.Singleton<ISchemaStore>(new InMemorySchemaStore(seeds)));
        }

        return this;
    }

    /// <summary>
    /// Adds <typeparamref name="TOperator"/> (Singleton). Its name must not clash with an operator that is still
    /// registered: to swap out an operator, remove it first (<c>RemoveOperator("eq").AddOperator&lt;MyEquals&gt;()</c>).
    /// </summary>
    public RuleLoomBuilder AddOperator<TOperator>() where TOperator : class, IConditionOperator
    {
        _registry.AddOperator(typeof(TOperator));
        return this;
    }

    /// <summary>
    /// Removes the operators registered so far (built-in or added) whose name matches, ignoring case.
    /// </summary>
    public RuleLoomBuilder RemoveOperator(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _registry.RemoveOperator(name);
        return this;
    }

    /// <summary>
    /// Removes all built-in operators, keeping operators added so far. Use it to start from a fully custom set.
    /// </summary>
    public RuleLoomBuilder RemoveBuiltInOperators()
    {
        _registry.RemoveBuiltInOperators();
        return this;
    }
}

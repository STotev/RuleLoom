using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Operators;
using RuleLoom.Schemas;

namespace RuleLoom;

/// <summary>
/// Builds a <see cref="Catalog"/>: event schemas, context providers, operators (the built-ins to start with) and actions.
/// Descriptions are found next to what they describe: an action's <see cref="ActionDescriptor{TAction}"/> in the action's
/// assembly, the <see cref="ContextSchema{T}"/> of what a provider loads in the provider's or that type's assembly, and
/// in assemblies added with <see cref="AddFromAssemblyOf{T}"/>. Registering the same thing twice is harmless;
/// conflicting registrations fail <see cref="Build"/>.
/// </summary>
public sealed class CatalogBuilder
{
    private readonly List<IEventSchema> _events = [];
    private readonly List<ProviderEntry> _providers = [];
    private readonly List<ActionEntry> _actions = [];
    private readonly List<IConditionOperator> _operators = [.. BuiltInOperators.Create()];
    private readonly List<IConditionOperator> _builtIns;
    private readonly List<Assembly> _assemblies = [];
    private JsonSerializerOptions? _options;

    /// <summary>
    /// Creates a builder with the built-in operators.
    /// </summary>
    public CatalogBuilder() => _builtIns = [.. _operators];

    /// <summary>
    /// The serializer options events and contexts are serialized with, which name the schemas' fields;
    /// <see cref="JsonSerializerDefaults.Web"/> when not set. Applies to every schema, whenever it's called.
    /// </summary>
    public CatalogBuilder UseJsonOptions(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        return this;
    }

    /// <summary>Adds an event schema class (an <see cref="EventSchema{T}"/>).</summary>
    public CatalogBuilder AddEvent<TSchema>() where TSchema : IEventSchema, new() => AddEvent(typeof(TSchema));

    /// <summary>
    /// Adds what an assembly defines: its event schemas and actions (under their default names), and the action
    /// descriptors and context schemas found there. Context providers need a name, so add them with
    /// <see cref="AddContextProvider{TProvider}"/>.
    /// </summary>
    public CatalogBuilder AddFromAssemblyOf<T>()
    {
        var assembly = typeof(T).Assembly;
        if (_assemblies.Contains(assembly))
            return this;

        _assemblies.Add(assembly);

        foreach (var type in Concrete(assembly).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (typeof(IEventSchema).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
                AddEvent(type);
            else if (typeof(IAction).IsAssignableFrom(type))
                AddAction(ActionNames.GetDefaultName(type), type, descriptor: null);
        }

        return this;
    }

    /// <summary>
    /// Adds <typeparamref name="TProvider"/> under <paramref name="name"/>, the name events refer to it by
    /// (<c>LoadedBy</c>, or a context of the same name). A <see cref="ContextProvider{TContext}"/> or
    /// <see cref="CollectionContextProvider{TItem}"/> is described by the <see cref="ContextSchema{T}"/> of the type it
    /// loads, or by that type alone; other providers have no schema (unless the catalog store has one).
    /// </summary>
    public CatalogBuilder AddContextProvider<TProvider>(string name) where TProvider : class, IContextProvider =>
        AddContextProvider(name, typeof(TProvider), schema: null);

    /// <summary>
    /// Adds <typeparamref name="TProvider"/> under <paramref name="name"/>, described by <typeparamref name="TSchema"/>,
    /// which must describe the type the provider loads.
    /// </summary>
    public CatalogBuilder AddContextProvider<TProvider, TSchema>(string name)
        where TProvider : class, IContextProvider where TSchema : IContextSchema, new() =>
        AddContextProvider(name, typeof(TProvider), typeof(TSchema));

    /// <summary>
    /// Adds <typeparamref name="TAction"/> under <paramref name="name"/>, or its default name
    /// (<see cref="ActionNames.GetDefaultName"/>), described by the <see cref="ActionDescriptor{TAction}"/> found in its
    /// assembly, if any. The same type may be added under several names.
    /// </summary>
    public CatalogBuilder AddAction<TAction>(string? name = null) where TAction : class, IAction =>
        AddAction(name ?? ActionNames.GetDefaultName(typeof(TAction)), typeof(TAction), descriptor: null);

    /// <summary>
    /// Adds <typeparamref name="TAction"/> like <see cref="AddAction{TAction}"/>, described by
    /// <typeparamref name="TDescriptor"/>.
    /// </summary>
    public CatalogBuilder AddAction<TAction, TDescriptor>(string? name = null)
        where TAction : class, IAction where TDescriptor : ActionDescriptor<TAction>, new() =>
        AddAction(name ?? ActionNames.GetDefaultName(typeof(TAction)), typeof(TAction), typeof(TDescriptor));

    /// <summary>
    /// Adds an operator. Its name must not clash with an operator that is still in the catalog: to swap out an operator,
    /// remove it first (<c>RemoveOperator("eq").AddOperator&lt;MyEquals&gt;()</c>).
    /// </summary>
    public CatalogBuilder AddOperator<TOperator>() where TOperator : IConditionOperator, new()
    {
        if (!_operators.Any(o => o.GetType() == typeof(TOperator)))
            _operators.Add(new TOperator());

        return this;
    }

    /// <summary>Adds an operator instance, e.g. one that needs configuration.</summary>
    public CatalogBuilder AddOperator(IConditionOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        if (!_operators.Contains(@operator))
            _operators.Add(@operator);

        return this;
    }

    /// <summary>Removes the operators added so far (built-in or not) whose name matches, ignoring case.</summary>
    public CatalogBuilder RemoveOperator(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _operators.RemoveAll(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        return this;
    }

    /// <summary>Removes the built-in operators, keeping those added so far. Use it to start from a fully custom set.</summary>
    public CatalogBuilder RemoveBuiltInOperators()
    {
        _operators.RemoveAll(_builtIns.Contains);
        return this;
    }

    /// <summary>
    /// Builds the catalog from what was added, without a store.
    /// </summary>
    /// <exception cref="InvalidOperationException">A registration is broken (see <see cref="Catalog"/>), a schema
    /// can't be generated, or a type is described twice.</exception>
    public Catalog Build() => Create(stored: null);

    /// <summary>
    /// Builds the catalog from what was added plus what <paramref name="store"/> holds (code wins over stored entries of
    /// the same name), then saves the result back to the store.
    /// </summary>
    /// <exception cref="InvalidOperationException">A registration is broken (see <see cref="Catalog"/>), a schema
    /// can't be generated, or a type is described twice.</exception>
    public async Task<Catalog> BuildAsync(ICatalogStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var stored = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var catalog = Create(stored);
        await store.SaveAsync(catalog.ToDocument(), cancellationToken).ConfigureAwait(false);

        return catalog;
    }

    private Catalog Create(CatalogDocument? stored)
    {
        var events = new SchemaCollection(_options);
        foreach (var schema in _events)
            events.Add(schema);

        var providers = _providers.Select(p => new ContextProviderRegistration(p.Name, p.Type, DescribeContext(p))).ToList();

        var actions = _actions.Select(a =>
        {
            var descriptor = Describe<IActionDescriptor>(a.Descriptor, typeof(ActionDescriptor<>), a.Type, "action", [a.Type.Assembly]);
            return new ActionRegistration(a.Name, a.Type, descriptor?.Label, descriptor?.Description);
        }).ToList();

        return new Catalog(events.Build().Events, providers, _operators, actions, stored);
    }

    private ContextSchemaDefinition? DescribeContext(ProviderEntry provider)
    {
        if (LoadedType(provider.Type) is not var (dataType, isMany))
        {
            return provider.Schema is null ? null
                : throw new InvalidOperationException(
                    $"Context provider '{provider.Name}' ({provider.Type.Name}) doesn't derive from ContextProvider<T> or " +
                    $"CollectionContextProvider<T>, so {provider.Schema.Name} can't describe it.");
        }

        var schema = Describe<IContextSchema>(provider.Schema, typeof(ContextSchema<>), dataType, "context",
            [provider.Type.Assembly, dataType.Assembly]);

        if (schema is not null && schema.DataType != dataType)
        {
            throw new InvalidOperationException(
                $"{schema.GetType().Name} describes {schema.DataType.Name}, but context provider '{provider.Name}' loads {dataType.Name}.");
        }

        if (schema is null && !HasFields(dataType))
            return null;

        schema ??= (IContextSchema)Activator.CreateInstance(typeof(InferredContextSchema<>).MakeGenericType(dataType))!;
        return schema.Build(provider.Name, isMany, _options);
    }

    // The explicit description, else the one found next to what it describes, or in a scanned assembly.
    private T? Describe<T>(Type? explicitType, Type descriptionDefinition, Type described, string what, Assembly[] near) where T : class
    {
        if (explicitType is not null)
            return (T)Activator.CreateInstance(explicitType)!;

        var description = descriptionDefinition.MakeGenericType(described);
        var found = near.Concat(_assemblies).Distinct()
            .SelectMany(Concrete)
            .Where(t => description.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

        return found.Count switch
        {
            0 => null,
            1 => (T)Activator.CreateInstance(found[0])!,
            _ => throw new InvalidOperationException(
                $"The {what} {described.Name} is described more than once: {string.Join(", ", found.Select(t => t.FullName))}.")
        };
    }

    private CatalogBuilder AddEvent(Type type)
    {
        if (!_events.Any(e => e.GetType() == type))
            _events.Add((IEventSchema)Activator.CreateInstance(type)!);

        return this;
    }

    private CatalogBuilder AddContextProvider(string name, Type type, Type? schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var entry = new ProviderEntry(name, type, schema);
        if (!_providers.Contains(entry))
            _providers.Add(entry);

        return this;
    }

    private CatalogBuilder AddAction(string name, Type type, Type? descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var entry = new ActionEntry(name, type, descriptor);
        if (!_actions.Contains(entry))
            _actions.Add(entry);

        return this;
    }

    private static IEnumerable<Type> Concrete(Assembly assembly) =>
        assembly.IsDynamic ? [] : assembly.GetExportedTypes().Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false });

    // What a typed provider loads: its type, and whether it loads a collection of it.
    private static (Type Type, bool IsMany)? LoadedType(Type providerType)
    {
        for (var type = providerType; type is not null; type = type.BaseType)
        {
            if (!type.IsGenericType)
                continue;

            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(ContextProvider<>))
                return (type.GetGenericArguments()[0], false);
            if (definition == typeof(CollectionContextProvider<>))
                return (type.GetGenericArguments()[0], true);
        }

        return null;
    }

    // Untyped data (object or JSON) has no fields to describe.
    private static bool HasFields(Type type) =>
        type != typeof(object) && type != typeof(JsonElement) && type != typeof(JsonDocument) && !typeof(JsonNode).IsAssignableFrom(type);

    private sealed record ProviderEntry(string Name, Type Type, Type? Schema);

    private sealed record ActionEntry(string Name, Type Type, Type? Descriptor);

    // Describes a context by its type alone, when no ContextSchema<T> is found.
    private sealed class InferredContextSchema<T> : ContextSchema<T>;
}

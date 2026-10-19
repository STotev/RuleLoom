using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Schemas;

namespace RuleLoom.Abstractions.Context;

/// <summary>
/// Base class for context providers that load one entity of a known type, e.g. <c>ContextProvider&lt;CustomerDto&gt;</c>.
/// The type describes the context's fields (its schema); override <see cref="Describe"/> to add labels or leave fields
/// out. For a collection, derive from <see cref="CollectionContextProvider{TItem}"/>.
/// </summary>
/// <typeparam name="TContext">The type of the loaded context.</typeparam>
public abstract class ContextProvider<TContext> : IContextProvider
{
    /// <summary>
    /// Loads the context for the event in <paramref name="request"/>.
    /// </summary>
    /// <returns>The context, or <c>null</c> (the default) if there is none.</returns>
    protected abstract ValueTask<TContext?> LoadAsync(ContextRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Describes the context: labels, descriptions and ignored fields. Fields are generated from
    /// <typeparamref name="TContext"/> without it.
    /// </summary>
    protected virtual void Describe(ContextDescription<TContext> context)
    {
    }

    async ValueTask<object?> IContextProvider.GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        await LoadAsync(request, cancellationToken).ConfigureAwait(false);

    ContextSchemaDefinition? IContextProvider.GetSchema(string name, JsonSerializerOptions? options)
    {
        if (!ContextTypes.HasFields(typeof(TContext)))
            return null;

        var description = new ContextDescription<TContext>();
        Describe(description);
        return description.Build(name, isMany: false, options);
    }
}

/// <summary>
/// Base class for context providers that load a collection of a known type, e.g. an ad's watchers
/// (<c>CollectionContextProvider&lt;WatcherDto&gt;</c>). The item type describes the context's fields (its schema);
/// override <see cref="Describe"/> to add labels or leave fields out.
/// </summary>
/// <typeparam name="TItem">The type of one item of the collection.</typeparam>
public abstract class CollectionContextProvider<TItem> : IContextProvider
{
    /// <summary>
    /// Loads the collection for the event in <paramref name="request"/>.
    /// </summary>
    /// <returns>The items; empty if there are none.</returns>
    protected abstract ValueTask<IReadOnlyList<TItem>> LoadAsync(ContextRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Describes the context: labels, descriptions and ignored fields of one item. Fields are generated from
    /// <typeparamref name="TItem"/> without it.
    /// </summary>
    protected virtual void Describe(ContextDescription<TItem> context)
    {
    }

    async ValueTask<object?> IContextProvider.GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        await LoadAsync(request, cancellationToken).ConfigureAwait(false);

    ContextSchemaDefinition? IContextProvider.GetSchema(string name, JsonSerializerOptions? options)
    {
        if (!ContextTypes.HasFields(typeof(TItem)))
            return null;

        var description = new ContextDescription<TItem>();
        Describe(description);
        return description.Build(name, isMany: true, options);
    }
}

internal static class ContextTypes
{
    /// <summary>
    /// Whether <paramref name="type"/> declares fields: untyped contexts (<see cref="object"/>, JSON) have no schema.
    /// </summary>
    public static bool HasFields(Type type) =>
        type != typeof(object) && type != typeof(JsonElement) && type != typeof(JsonDocument) && !typeof(JsonNode).IsAssignableFrom(type);
}

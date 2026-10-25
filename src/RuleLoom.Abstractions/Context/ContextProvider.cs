namespace RuleLoom.Abstractions.Context;

/// <summary>
/// Base class for context providers that load one entity of a known type, e.g. <c>ContextProvider&lt;CustomerDto&gt;</c>.
/// The type describes the context's fields (its schema); add labels or leave fields out with a
/// <c>ContextSchema&lt;TContext&gt;</c>. For a collection, derive from <see cref="CollectionContextProvider{TItem}"/>.
/// </summary>
/// <typeparam name="TContext">The type of the loaded context.</typeparam>
public abstract class ContextProvider<TContext> : IContextProvider
{
    /// <summary>
    /// Loads the context for the event in <paramref name="request"/>.
    /// </summary>
    /// <returns>The context, or <c>null</c> (the default) if there is none.</returns>
    protected abstract ValueTask<TContext?> LoadAsync(ContextRequest request, CancellationToken cancellationToken);

    async ValueTask<object?> IContextProvider.GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        await LoadAsync(request, cancellationToken).ConfigureAwait(false);
}

/// <summary>
/// Base class for context providers that load a collection of a known type, e.g. an ad's watchers
/// (<c>CollectionContextProvider&lt;WatcherDto&gt;</c>). The item type describes the context's fields (its schema); add
/// labels or leave fields out with a <c>ContextSchema&lt;TItem&gt;</c>.
/// </summary>
/// <typeparam name="TItem">The type of one item of the collection.</typeparam>
public abstract class CollectionContextProvider<TItem> : IContextProvider
{
    /// <summary>
    /// Loads the collection for the event in <paramref name="request"/>.
    /// </summary>
    /// <returns>The items; empty if there are none.</returns>
    protected abstract ValueTask<IReadOnlyList<TItem>> LoadAsync(ContextRequest request, CancellationToken cancellationToken);

    async ValueTask<object?> IContextProvider.GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        await LoadAsync(request, cancellationToken).ConfigureAwait(false);
}

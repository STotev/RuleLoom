namespace RuleLoom.Abstractions.Context;

/// <summary>
/// Base class for context providers that load a known type, e.g. <c>ContextProvider&lt;CustomerDto&gt;</c>. The engine
/// treats the result like any other context; the type documents what the provider loads.
/// </summary>
/// <typeparam name="TContext">The type of the loaded context (a collection type for collection contexts).</typeparam>
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

namespace RuleLoom.Abstractions.Context;

/// <summary>
/// Loads a context: data related to an event's subject that rules use with <c>@name</c> paths (e.g. the order's
/// customer for <c>@customer.Tier</c>). Registered under a name; the engine calls it only when a rule or action uses
/// that context, at most once per event.
/// </summary>
public interface IContextProvider
{
    /// <summary>
    /// Loads the context for the event in <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The context name and the event (type, current and previous state).</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The context (a .NET object, JSON or a collection), or <c>null</c> if there is none: paths into it
    /// then resolve to <c>null</c>.</returns>
    ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken);
}

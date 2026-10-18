namespace RuleLoom.Abstractions.Context;

/// <summary>
/// Tells the engine how an event refers to a context (e.g. from the event's schema), so the resolved reference can be
/// passed to the context provider as <see cref="ContextRequest.Reference"/>.
/// </summary>
public interface IContextReferenceSource
{
    /// <summary>
    /// Gets the reference declared for <paramref name="contextName"/> on <paramref name="eventType"/>.
    /// </summary>
    /// <returns>The reference, or <c>null</c> if the event or context is unknown or declares no reference.</returns>
    ValueTask<ContextReference?> GetReferenceAsync(string eventType, string contextName, CancellationToken cancellationToken);
}

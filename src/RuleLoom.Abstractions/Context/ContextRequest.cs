namespace RuleLoom.Abstractions.Context;

/// <summary>
/// What an <see cref="IContextProvider"/> gets: the context's name and the event it's loaded for.
/// </summary>
/// <param name="Name">The context name as registered (e.g. <c>customer</c>).</param>
/// <param name="EventType">The event being processed.</param>
/// <param name="Current">The subject's current state.</param>
/// <param name="Previous">The subject's previous state, if any.</param>
public sealed record ContextRequest(string Name, string EventType, object? Current, object? Previous)
{
    /// <summary>
    /// Resolves a path on the subject (e.g. <c>CustomerId</c> or <c>$previous.CustomerId</c>), with the same rules as
    /// rule paths. Contexts can't be resolved from here.
    /// </summary>
    public object? Resolve(string path) => new EvaluationContext(Current, Previous).Resolve(path);
}

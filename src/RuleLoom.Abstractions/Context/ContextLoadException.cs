namespace RuleLoom.Abstractions.Context;

/// <summary>
/// A context provider failed to load a context. Rules and actions that use the context fail with this error; others
/// still run.
/// </summary>
public sealed class ContextLoadException : Exception
{
    /// <summary>
    /// Creates the exception for the context <paramref name="name"/>.
    /// </summary>
    public ContextLoadException(string name, Exception innerException)
        : base($"Context '@{name}' couldn't be loaded: {innerException.Message}", innerException)
    {
        Name = name;
    }

    /// <summary>
    /// The context that failed to load.
    /// </summary>
    public string Name { get; }
}

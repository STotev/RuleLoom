namespace RuleLoom.Abstractions.Context;

/// <summary>
/// Resolves context providers by the name they are registered under.
/// </summary>
public interface IContextProviderFactory
{
    /// <summary>
    /// Tries to get the provider registered under <paramref name="name"/> (matched ignoring case).
    /// </summary>
    /// <param name="name">The context name (e.g. "customer").</param>
    /// <param name="provider">The resolved provider when found.</param>
    /// <returns><c>true</c> when a provider is found; otherwise <c>false</c>.</returns>
    bool TryGetProvider(string name, out IContextProvider? provider);

    /// <summary>
    /// The context names this factory can resolve.
    /// </summary>
    IReadOnlyCollection<string> ProviderNames { get; }
}

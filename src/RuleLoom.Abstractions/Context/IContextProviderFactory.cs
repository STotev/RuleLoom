namespace RuleLoom.Abstractions.Context;

/// <summary>
/// Creates context providers. The engine finds a provider's registration in its <see cref="Catalog"/> and asks the
/// factory for an instance, so the factory never resolves names: implement it to create providers your own way (e.g.
/// from another container).
/// </summary>
public interface IContextProviderFactory
{
    /// <summary>
    /// Creates (or returns) the provider for <paramref name="provider"/>.
    /// </summary>
    /// <param name="provider">The provider's registration in the catalog.</param>
    /// <exception cref="InvalidOperationException">The provider can't be created.</exception>
    IContextProvider Create(ContextProviderRegistration provider);
}

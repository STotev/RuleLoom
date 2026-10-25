using RuleLoom.Abstractions.Context;

namespace RuleLoom.Contexts;

/// <summary>
/// Default non-DI context provider factory: returns the instance added for a provider's type, or else creates the type
/// with its parameterless constructor. Names stay in the catalog; the factory only knows types.
/// </summary>
public sealed class DirectContextProviderFactory : IContextProviderFactory
{
    private readonly Dictionary<Type, IContextProvider> _providers = [];

    /// <summary>
    /// Returns <paramref name="provider"/> for every registration of its type.
    /// </summary>
    /// <exception cref="ArgumentException">An instance of this type was already added.</exception>
    public DirectContextProviderFactory Add(IContextProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_providers.TryAdd(provider.GetType(), provider))
            throw new ArgumentException($"An instance of {provider.GetType().Name} was already added.", nameof(provider));

        return this;
    }

    /// <inheritdoc />
    public IContextProvider Create(ContextProviderRegistration provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return _providers.TryGetValue(provider.Type, out var instance)
            ? instance
            : Activation.Create<IContextProvider>(provider.Type, provider.Name);
    }
}

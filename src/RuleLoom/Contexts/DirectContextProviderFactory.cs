using RuleLoom.Abstractions.Context;

namespace RuleLoom.Contexts;

/// <summary>
/// Default non-DI context provider factory backed by provider instances, each registered under a name. Names are
/// matched ignoring case.
/// </summary>
public sealed class DirectContextProviderFactory : IContextProviderFactory
{
    private readonly Dictionary<string, IContextProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> ProviderNames => _providers.Keys;

    /// <summary>
    /// Registers <paramref name="provider"/> for the context <paramref name="name"/> (used in rules as <c>@name</c>).
    /// </summary>
    /// <exception cref="ArgumentException">The name is already registered (names are case-insensitive).</exception>
    public DirectContextProviderFactory Add(IContextProvider provider, string name)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_providers.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            throw new ArgumentException(
                existing == name
                    ? $"Context name '{name}' is registered more than once."
                    : $"Context name '{name}' conflicts with '{existing}' (names are case-insensitive).",
                nameof(name));
        }

        _providers.Add(name, provider);
        return this;
    }

    public bool TryGetProvider(string name, out IContextProvider? provider) =>
        _providers.TryGetValue(name, out provider);
}

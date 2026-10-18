using RuleLoom.Abstractions.Context;
using RuleLoom.Schemas;

namespace RuleLoom.Contexts;

/// <summary>
/// Resolves contexts through the event's schema: the schema says which provider loads a context (<c>LoadedBy</c>,
/// otherwise the provider registered under the context's name) and how the event refers to it (<c>ReferencedBy</c>,
/// resolved on the current state and passed as <see cref="ContextRequest.Reference"/>). One provider can so serve several
/// contexts, e.g. <c>fromAccount</c> and <c>toAccount</c> both loaded by <c>account</c>.
/// </summary>
/// <remarks>
/// Which provider loads a context is only known once the schema is read, so every context name gets a provider here; a
/// context whose provider isn't registered fails when it loads (only the rules that use it fail).
/// </remarks>
internal sealed class SchemaReferencingProviders(IContextProviderFactory providers, ISchemaStore schemas) : IContextProviderFactory
{
    public IReadOnlyCollection<string> ProviderNames => providers.ProviderNames;

    public bool TryGetProvider(string name, out IContextProvider? provider)
    {
        provider = new Referencing(providers, schemas);
        return true;
    }

    private sealed class Referencing(IContextProviderFactory providers, ISchemaStore schemas) : IContextProvider
    {
        public async ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken)
        {
            var schema = await schemas.GetAsync(request.EventType, cancellationToken).ConfigureAwait(false);
            var declaration = schema?.Contexts.FirstOrDefault(c => string.Equals(c.Name, request.Name, StringComparison.OrdinalIgnoreCase));
            var providerName = declaration?.Provider ?? request.Name;

            if (!providers.TryGetProvider(providerName, out var provider) || provider is null)
            {
                throw new InvalidOperationException(providerName == request.Name
                    ? $"No context provider is registered for '@{request.Name}'."
                    : $"No context provider is registered under '{providerName}' for '@{request.Name}'.");
            }

            if (declaration?.ReferencedBy is not { } reference)
                return await provider.GetAsync(request, cancellationToken).ConfigureAwait(false);

            object? resolved = reference.IsComposite
                ? reference.Parts.ToDictionary(part => part.Key, part => request.Resolve(part.Value), StringComparer.OrdinalIgnoreCase)
                    as IReadOnlyDictionary<string, object?>
                : request.Resolve(reference.Path!);

            return await provider.GetAsync(request with { Reference = resolved }, cancellationToken).ConfigureAwait(false);
        }
    }
}

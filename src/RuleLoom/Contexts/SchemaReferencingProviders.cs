using RuleLoom.Abstractions.Context;
using RuleLoom.Schemas;

namespace RuleLoom.Contexts;

/// <summary>
/// Wraps context providers so each one receives the event's declared reference to its context: the event schema's
/// <c>ReferencedBy</c> paths, resolved on the event's current state, as <see cref="ContextRequest.Reference"/>.
/// </summary>
internal sealed class SchemaReferencingProviders(IContextProviderFactory providers, ISchemaStore schemas) : IContextProviderFactory
{
    public IReadOnlyCollection<string> ProviderNames => providers.ProviderNames;

    public bool TryGetProvider(string name, out IContextProvider? provider)
    {
        provider = providers.TryGetProvider(name, out var inner) && inner is not null ? new Referencing(inner, schemas) : null;
        return provider is not null;
    }

    private sealed class Referencing(IContextProvider inner, ISchemaStore schemas) : IContextProvider
    {
        public async ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken)
        {
            var schema = await schemas.GetAsync(request.EventType, cancellationToken).ConfigureAwait(false);
            var reference = schema?.Contexts
                .FirstOrDefault(c => string.Equals(c.Name, request.Name, StringComparison.OrdinalIgnoreCase))?
                .ReferencedBy;

            if (reference is null)
                return await inner.GetAsync(request, cancellationToken).ConfigureAwait(false);

            object? resolved = reference.IsComposite
                ? reference.Parts.ToDictionary(part => part.Key, part => request.Resolve(part.Value), StringComparer.OrdinalIgnoreCase)
                    as IReadOnlyDictionary<string, object?>
                : request.Resolve(reference.Path!);

            return await inner.GetAsync(request with { Reference = resolved }, cancellationToken).ConfigureAwait(false);
        }
    }
}

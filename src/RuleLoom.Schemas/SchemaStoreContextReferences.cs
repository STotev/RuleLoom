using RuleLoom.Abstractions.Context;

namespace RuleLoom.Schemas;

/// <summary>
/// Gives the engine each context's reference from the event schemas in an <see cref="ISchemaStore"/>, so providers get
/// the resolved reference as <see cref="ContextRequest.Reference"/>.
/// </summary>
public sealed class SchemaStoreContextReferences(ISchemaStore store) : IContextReferenceSource
{
    public async ValueTask<ContextReference?> GetReferenceAsync(string eventType, string contextName, CancellationToken cancellationToken)
    {
        var schema = await store.GetAsync(eventType, cancellationToken).ConfigureAwait(false);

        return schema?.Contexts
            .FirstOrDefault(c => string.Equals(c.Name, contextName, StringComparison.OrdinalIgnoreCase))?
            .ReferencedBy;
    }
}

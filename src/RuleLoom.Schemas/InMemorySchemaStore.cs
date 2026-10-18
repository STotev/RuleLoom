using System.Collections.Concurrent;

namespace RuleLoom.Schemas;

/// <summary>
/// A thread-safe <see cref="ISchemaStore"/> that keeps schemas in memory, for tests, samples and services that load
/// their schemas at startup.
/// </summary>
public sealed class InMemorySchemaStore : ISchemaStore
{
    private readonly ConcurrentDictionary<string, EventSchemaDefinition> _schemas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a store holding <paramref name="schemas"/> (later ones replace earlier ones of the same event type).</summary>
    public InMemorySchemaStore(IEnumerable<EventSchemaDefinition>? schemas = null)
    {
        foreach (var schema in schemas ?? [])
            _schemas[schema.EventType] = schema;
    }

    public ValueTask<EventSchemaDefinition?> GetAsync(string eventType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return ValueTask.FromResult(_schemas.GetValueOrDefault(eventType));
    }

    public ValueTask<IReadOnlyList<EventSchemaDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<EventSchemaDefinition>>(_schemas.Values.OrderBy(s => s.EventType, StringComparer.Ordinal).ToList());

    public ValueTask SaveAsync(EventSchemaDefinition schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schemas[schema.EventType] = schema;
        return ValueTask.CompletedTask;
    }
}

using System.Collections.Concurrent;

namespace RuleLoom.Schemas;

/// <summary>
/// A thread-safe <see cref="ISchemaStore"/> that keeps schemas in memory, for tests, samples and services that load
/// their schemas at startup.
/// </summary>
public sealed class InMemorySchemaStore : ISchemaStore
{
    private readonly ConcurrentDictionary<string, EventSchemaDefinition> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ContextSchemaDefinition> _contexts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a store holding <paramref name="schemas"/>.</summary>
    public InMemorySchemaStore(SchemaSet? schemas = null)
    {
        foreach (var schema in schemas?.Events ?? [])
            _events[schema.EventType] = schema;
        foreach (var schema in schemas?.Contexts ?? [])
            _contexts[schema.Name] = schema;
    }

    public ValueTask<EventSchemaDefinition?> GetEventAsync(string eventType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return ValueTask.FromResult(_events.GetValueOrDefault(eventType));
    }

    public ValueTask<IReadOnlyList<EventSchemaDefinition>> ListEventsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<EventSchemaDefinition>>(_events.Values.OrderBy(s => s.EventType, StringComparer.Ordinal).ToList());

    public ValueTask SaveEventAsync(EventSchemaDefinition schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _events[schema.EventType] = schema;
        return ValueTask.CompletedTask;
    }

    public ValueTask<ContextSchemaDefinition?> GetContextAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ValueTask.FromResult(_contexts.GetValueOrDefault(name));
    }

    public ValueTask<IReadOnlyList<ContextSchemaDefinition>> ListContextsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ContextSchemaDefinition>>(_contexts.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToList());

    public ValueTask SaveContextAsync(ContextSchemaDefinition schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _contexts[schema.Name] = schema;
        return ValueTask.CompletedTask;
    }
}

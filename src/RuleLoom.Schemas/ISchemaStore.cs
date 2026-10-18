namespace RuleLoom.Schemas;

/// <summary>
/// Where the service that runs the rules keeps event and context schemas. RuleLoom provides
/// <see cref="InMemorySchemaStore"/>; implement it for other storage (tenancy goes inside the implementation).
/// </summary>
public interface ISchemaStore
{
    /// <summary>Gets the schema of <paramref name="eventType"/> (matched ignoring case), or <c>null</c>.</summary>
    ValueTask<EventSchemaDefinition?> GetEventAsync(string eventType, CancellationToken cancellationToken = default);

    /// <summary>Lists every stored event schema.</summary>
    ValueTask<IReadOnlyList<EventSchemaDefinition>> ListEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves <paramref name="schema"/>, replacing a stored schema of the same event type.</summary>
    ValueTask SaveEventAsync(EventSchemaDefinition schema, CancellationToken cancellationToken = default);

    /// <summary>Gets the schema of the context loaded by provider <paramref name="name"/> (matched ignoring case), or <c>null</c>.</summary>
    ValueTask<ContextSchemaDefinition?> GetContextAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Lists every stored context schema.</summary>
    ValueTask<IReadOnlyList<ContextSchemaDefinition>> ListContextsAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves <paramref name="schema"/>, replacing a stored schema of the same context.</summary>
    ValueTask SaveContextAsync(ContextSchemaDefinition schema, CancellationToken cancellationToken = default);
}

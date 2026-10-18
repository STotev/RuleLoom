namespace RuleLoom.Schemas;

/// <summary>
/// Where the service that runs the rules keeps event schemas. Implement it on your own storage (tenancy goes inside the
/// implementation); <see cref="InMemorySchemaStore"/> keeps them in memory.
/// </summary>
public interface ISchemaStore
{
    /// <summary>Gets the schema of <paramref name="eventType"/> (matched ignoring case), or <c>null</c>.</summary>
    ValueTask<EventSchemaDefinition?> GetAsync(string eventType, CancellationToken cancellationToken = default);

    /// <summary>Lists every stored schema.</summary>
    ValueTask<IReadOnlyList<EventSchemaDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves <paramref name="schema"/>, replacing a stored schema of the same event type.</summary>
    ValueTask SaveAsync(EventSchemaDefinition schema, CancellationToken cancellationToken = default);
}

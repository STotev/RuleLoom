namespace RuleLoom.Schemas;

/// <summary>
/// A thread-safe <see cref="ICatalogStore"/> that keeps the catalog in memory, for tests, samples and apps that don't
/// keep it elsewhere.
/// </summary>
public sealed class InMemoryCatalogStore : ICatalogStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, EventSchemaDefinition> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ContextSchemaDefinition> _contexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CatalogOperator> _operators = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CatalogAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask<CatalogDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
            return ValueTask.FromResult(new CatalogDocument(_events.Values, _contexts.Values, _operators.Values, _actions.Values));
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(CatalogDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_lock)
        {
            Upsert(_events, document.Events, e => e.EventType);
            Upsert(_contexts, document.Contexts, c => c.Name);
            Upsert(_operators, document.Operators, o => o.Name);
            Upsert(_actions, document.Actions, a => a.Name);
        }

        return ValueTask.CompletedTask;
    }

    // Replacing removes the old key first, so the stored name takes the new spelling.
    private static void Upsert<T>(Dictionary<string, T> entries, IEnumerable<T> items, Func<T, string> name)
    {
        foreach (var item in items)
        {
            entries.Remove(name(item));
            entries[name(item)] = item;
        }
    }
}

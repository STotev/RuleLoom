using System.Text.Json;
using RuleLoom.Schemas;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// Configures the event schemas for <see cref="RuleLoomBuilder.AddSchemas"/>: where schemas come from (schema classes,
/// exported JSON) and where they're kept (the in-memory store, or your own <see cref="ISchemaStore"/>). The sources are
/// saved into the store: the in-memory store starts with them; your own store gets them at startup.
/// </summary>
public sealed class SchemaRegistration
{
    private readonly List<Action<EventSchemaCollection>> _classes = [];
    private readonly List<string> _json = [];

    internal Type? StoreType { get; private set; }

    /// <summary>The serializer options events are sent with, for building schema classes (Web defaults when unset).</summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>Adds a schema class as a source.</summary>
    public SchemaRegistration Add<TSchema>() where TSchema : IEventSchema, new()
    {
        _classes.Add(collection => collection.Add<TSchema>());
        return this;
    }

    /// <summary>Adds the schema classes in the assembly that contains <typeparamref name="T"/> as sources.</summary>
    public SchemaRegistration AddFromAssemblyOf<T>()
    {
        _classes.Add(collection => collection.AddFromAssemblyOf<T>());
        return this;
    }

    /// <summary>Adds exported schemas (a JSON array, as written by <see cref="EventSchemaCollection.ToJson"/>) as a source.</summary>
    public SchemaRegistration AddJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        _json.Add(json);
        return this;
    }

    /// <summary>
    /// Keeps schemas in <typeparamref name="TStore"/> (Scoped) instead of the in-memory store, e.g. a database-backed
    /// store. The sources are saved into it at startup (replacing stored schemas of the same event type).
    /// </summary>
    public SchemaRegistration UseStore<TStore>() where TStore : class, ISchemaStore
    {
        StoreType = typeof(TStore);
        return this;
    }

    /// <summary>
    /// Builds the schema classes and reads the JSON.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two schemas describe the same event type.</exception>
    internal IReadOnlyList<EventSchemaDefinition> BuildSources()
    {
        var collection = new EventSchemaCollection(SerializerOptions);
        foreach (var add in _classes)
            add(collection);

        var schemas = collection.Build().Concat(_json.SelectMany(EventSchemaDefinition.ParseMany)).ToList();

        var duplicate = schemas.GroupBy(s => s.EventType, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Event '{duplicate.Key}' is described by more than one schema.");

        return schemas;
    }
}

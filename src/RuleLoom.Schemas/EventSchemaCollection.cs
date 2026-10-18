using System.Reflection;
using System.Text.Json;

namespace RuleLoom.Schemas;

/// <summary>
/// Gathers a producer's event schemas, builds them and writes them as JSON for a central rules service.
/// </summary>
/// <param name="options">The serializer options the producer sends events with; <see cref="JsonSerializerDefaults.Web"/>
/// when omitted.</param>
public sealed class EventSchemaCollection(JsonSerializerOptions? options = null)
{
    private readonly List<IEventSchema> _schemas = [];

    /// <summary>Adds a schema class.</summary>
    public EventSchemaCollection Add<TSchema>() where TSchema : IEventSchema, new() => Add(new TSchema());

    /// <summary>Adds a schema instance.</summary>
    public EventSchemaCollection Add(IEventSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schemas.Add(schema);
        return this;
    }

    /// <summary>
    /// Adds every public, non-abstract, non-generic schema class in <paramref name="assembly"/> that has a public
    /// parameterless constructor, ordered by name.
    /// </summary>
    public EventSchemaCollection AddFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var types = assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } && typeof(IEventSchema).IsAssignableFrom(t) &&
                        t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in types)
            Add((IEventSchema)Activator.CreateInstance(type)!);

        return this;
    }

    /// <summary>Adds the schema classes in the assembly that contains <typeparamref name="T"/>.</summary>
    public EventSchemaCollection AddFromAssemblyOf<T>() => AddFromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Builds every schema.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two schemas describe the same event type (ignoring case), or a
    /// schema is invalid.</exception>
    public IReadOnlyList<EventSchemaDefinition> Build()
    {
        // One read-only copy for every schema, so they share the serializer's metadata.
        var shared = options is null ? null : SchemaGenerator.Freeze(options);
        var built = _schemas.Select(schema => schema.Build(shared)).ToList();

        var duplicate = built.GroupBy(s => s.EventType, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Event '{duplicate.Key}' is described by more than one schema.");

        return built;
    }

    /// <summary>
    /// Builds every schema and writes them as a JSON array of event documents (read back with
    /// <see cref="EventSchemaDefinition.ParseMany"/>).
    /// </summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, EventSchemaDefinition.WriterOptions))
        {
            writer.WriteStartArray();
            foreach (var schema in Build())
                schema.WriteTo(writer);
            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}

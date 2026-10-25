using System.Reflection;
using System.Text.Json;

namespace RuleLoom.Schemas;

/// <summary>
/// Gathers event schema classes, builds them and writes them as JSON, without the engine (e.g. in a service that only
/// produces events). Catalogs use it for their event schemas.
/// </summary>
/// <param name="options">The serializer options the events are produced with; <see cref="JsonSerializerDefaults.Web"/>
/// when omitted.</param>
public sealed class SchemaCollection(JsonSerializerOptions? options = null)
{
    private readonly List<IEventSchema> _events = [];

    /// <summary>Adds an event schema class.</summary>
    public SchemaCollection Add<TSchema>() where TSchema : IEventSchema, new() => Add(new TSchema());

    /// <summary>Adds an event schema instance.</summary>
    public SchemaCollection Add(IEventSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _events.Add(schema);
        return this;
    }

    /// <summary>
    /// Adds every public, non-abstract, non-generic event schema class in <paramref name="assembly"/> that has a public
    /// parameterless constructor, ordered by name.
    /// </summary>
    public SchemaCollection AddFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var types = assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } &&
                        typeof(IEventSchema).IsAssignableFrom(t) &&
                        t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in types)
            Add((IEventSchema)Activator.CreateInstance(type)!);

        return this;
    }

    /// <summary>Adds the schema classes in the assembly that contains <typeparamref name="T"/>.</summary>
    public SchemaCollection AddFromAssemblyOf<T>() => AddFromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Builds every schema into a catalog document that holds only events.
    /// </summary>
    /// <exception cref="InvalidOperationException">An event type is described twice (ignoring case), or a schema is
    /// invalid.</exception>
    public CatalogDocument Build()
    {
        // One read-only copy for every schema, so they share the serializer's metadata.
        var shared = options is null ? null : SchemaGenerator.Freeze(options);

        return new CatalogDocument(_events.Select(s => s.Build(shared)));
    }

    /// <summary>
    /// Builds every schema and writes them as a catalog document (read back with <see cref="CatalogDocument.Parse"/>).
    /// </summary>
    public string ToJson() => Build().ToJson();
}

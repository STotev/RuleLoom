using System.Reflection;
using System.Text.Json;

namespace RuleLoom.Schemas;

/// <summary>
/// Gathers event and context schema classes, builds them and writes them as JSON.
/// </summary>
/// <param name="options">The serializer options the events and contexts are produced with;
/// <see cref="JsonSerializerDefaults.Web"/> when omitted.</param>
public sealed class SchemaCollection(JsonSerializerOptions? options = null)
{
    private readonly List<IEventSchema> _events = [];
    private readonly List<IContextSchema> _contexts = [];

    /// <summary>Adds a schema class (an <see cref="EventSchema{T}"/> or a <see cref="ContextSchema{T}"/>).</summary>
    /// <exception cref="ArgumentException"><typeparamref name="TSchema"/> is neither an event nor a context schema.</exception>
    public SchemaCollection Add<TSchema>() where TSchema : class, new() => Add(new TSchema());

    /// <summary>Adds an event schema instance.</summary>
    public SchemaCollection Add(IEventSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _events.Add(schema);
        return this;
    }

    /// <summary>Adds a context schema instance.</summary>
    public SchemaCollection Add(IContextSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _contexts.Add(schema);
        return this;
    }

    /// <summary>
    /// Adds every public, non-abstract, non-generic event and context schema class in <paramref name="assembly"/> that
    /// has a public parameterless constructor, ordered by name.
    /// </summary>
    public SchemaCollection AddFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var types = assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } &&
                        (typeof(IEventSchema).IsAssignableFrom(t) || typeof(IContextSchema).IsAssignableFrom(t)) &&
                        t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in types)
            Add(Activator.CreateInstance(type)!);

        return this;
    }

    /// <summary>Adds the schema classes in the assembly that contains <typeparamref name="T"/>.</summary>
    public SchemaCollection AddFromAssemblyOf<T>() => AddFromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Builds every schema.
    /// </summary>
    /// <exception cref="InvalidOperationException">An event type or context name is described twice (ignoring case), or a
    /// schema is invalid.</exception>
    public SchemaSet Build()
    {
        // One read-only copy for every schema, so they share the serializer's metadata.
        var shared = options is null ? null : SchemaGenerator.Freeze(options);

        return new SchemaSet(_events.Select(s => s.Build(shared)), _contexts.Select(s => s.Build(shared)));
    }

    /// <summary>
    /// Builds every schema and writes them as one JSON array (read back with <see cref="SchemaSet.Parse"/>).
    /// </summary>
    public string ToJson() => Build().ToJson();

    private SchemaCollection Add(object schema) => schema switch
    {
        IEventSchema @event => Add(@event),
        IContextSchema context => Add(context),
        _ => throw new ArgumentException($"{schema.GetType().Name} is neither an event schema nor a context schema.", nameof(schema))
    };
}

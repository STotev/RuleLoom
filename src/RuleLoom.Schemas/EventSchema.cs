using System.Linq.Expressions;
using System.Text.Json;

namespace RuleLoom.Schemas;

/// <summary>
/// Base class for describing an event whose subject is <typeparamref name="T"/>. Configure it in the constructor:
/// <code>
/// public sealed class OrderPlacedSchema : EventSchema&lt;OrderDto&gt;
/// {
///     public OrderPlacedSchema()
///     {
///         Event("OrderPlaced").Label("Order placed");
///         Field(o =&gt; o.Total).Label("Order total");
///         Field(o =&gt; o.InternalNotes).Ignore();
///         Context&lt;CustomerDto&gt;("customer");
///     }
/// }
/// </code>
/// Fields are generated from how <typeparamref name="T"/> is serialized (System.Text.Json's schema exporter);
/// the configuration adds labels and descriptions and leaves fields out.
/// </summary>
/// <typeparam name="T">The event's subject type (usually a DTO).</typeparam>
public abstract class EventSchema<T> : IEventSchema
{
    private readonly FieldConfigurations _fields = new();
    private readonly List<ContextBuilder> _contexts = [];
    private EventBuilder? _event;

    /// <summary>
    /// Names the event (required): the event type rules are written for.
    /// </summary>
    /// <exception cref="InvalidOperationException">The event is already named.</exception>
    protected EventBuilder Event(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        if (_event is not null)
            throw new InvalidOperationException($"The event is already named '{_event.EventType}'.");

        return _event = new EventBuilder(eventType);
    }

    /// <summary>
    /// Configures a field of the subject, at any depth (<c>o =&gt; o.Customer.Tier</c>).
    /// </summary>
    protected FieldBuilder Field<TProperty>(Expression<Func<T, TProperty>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return _fields.Field(property);
    }

    /// <summary>
    /// Declares a context the event's rules can use as <c>@name</c>, with its fields generated from
    /// <typeparamref name="TContext"/> and optionally configured.
    /// </summary>
    /// <exception cref="ArgumentException">A context with this name is already declared (names ignore case).</exception>
    protected ContextBuilder Context<TContext>(string name, Action<ContextFields<TContext>>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_contexts.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Context '{name}' is already declared (names ignore case).", nameof(name));

        var fields = new ContextFields<TContext>(new FieldConfigurations());
        configure?.Invoke(fields);

        var context = new ContextBuilder(name, typeof(TContext), fields.Fields);
        _contexts.Add(context);
        return context;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The event wasn't named, or a configured field isn't part of the
    /// serialized form.</exception>
    public EventSchemaDefinition Build(JsonSerializerOptions? options = null)
    {
        if (_event is null)
            throw new InvalidOperationException($"{GetType().Name} doesn't name its event; call Event(\"...\") in its constructor.");

        return SchemaGenerator.Generate(_event, typeof(T), _fields, _contexts, options);
    }
}

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
///         Context("customer").ReferencedBy(o =&gt; o.CustomerId);
///     }
/// }
/// </code>
/// Fields are generated from how <typeparamref name="T"/> is serialized (System.Text.Json's schema exporter);
/// the configuration adds labels and descriptions and leaves fields out. Only configured labels are written.
/// </summary>
/// <typeparam name="T">The event's subject type (usually a DTO).</typeparam>
public abstract class EventSchema<T> : IEventSchema
{
    private readonly FieldConfigurations _fields = new();
    private readonly List<ContextBuilder<T>> _contexts = [];
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
    /// Declares a context the event relates to: an entity rules can use as <c>@name</c>, loaded by the context provider
    /// registered under that name. Its fields are described where the provider is registered.
    /// </summary>
    /// <exception cref="ArgumentException">A context with this name is already declared (names ignore case).</exception>
    protected ContextBuilder<T> Context(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_contexts.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Context '{name}' is already declared (names ignore case).", nameof(name));

        var context = new ContextBuilder<T>(name);
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

        return SchemaGenerator.Generate(_event, typeof(T), _fields,
            _contexts.Select(c => new ContextDeclaration(c.Name, c.ConfiguredLabel, c.ConfiguredDescription, c.Reference)).ToList(),
            options);
    }
}

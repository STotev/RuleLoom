using System.Linq.Expressions;

namespace RuleLoom.Schemas;

/// <summary>
/// Describes the event: returned by <see cref="EventSchema{T}"/>'s <c>Event(...)</c>.
/// </summary>
public sealed class EventBuilder
{
    internal string? ConfiguredLabel { get; private set; }
    internal string? ConfiguredDescription { get; private set; }

    internal EventBuilder(string eventType) => EventType = eventType;

    internal string EventType { get; }

    /// <summary>A readable name for authoring UIs (written as <c>title</c>).</summary>
    public EventBuilder Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ConfiguredLabel = label;
        return this;
    }

    /// <summary>A longer explanation of the event.</summary>
    public EventBuilder Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ConfiguredDescription = description;
        return this;
    }
}

/// <summary>
/// Configures a field: returned by <c>Field(...)</c>. Configuring the same field again changes the same settings.
/// </summary>
public sealed class FieldBuilder
{
    private readonly FieldConfiguration _configuration;

    internal FieldBuilder(FieldConfiguration configuration) => _configuration = configuration;

    /// <summary>A readable name for authoring UIs (written as <c>title</c>).</summary>
    public FieldBuilder Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _configuration.Label = label;
        return this;
    }

    /// <summary>A longer explanation of the field.</summary>
    public FieldBuilder Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _configuration.Description = description;
        return this;
    }

    /// <summary>Leaves the field out of the schema, so rules can't use it.</summary>
    public FieldBuilder Ignore()
    {
        _configuration.Ignored = true;
        return this;
    }
}

/// <summary>
/// Describes a context the event relates to: returned by <see cref="EventSchema{T}"/>'s <c>Context(...)</c>.
/// </summary>
/// <typeparam name="T">The event's subject type.</typeparam>
public sealed class ContextBuilder<T>
{
    internal ContextBuilder(string name) => Name = name;

    internal string Name { get; }
    internal string? ConfiguredLabel { get; private set; }
    internal string? ConfiguredDescription { get; private set; }
    internal LambdaExpression? Reference { get; private set; }

    /// <summary>A readable name for authoring UIs (written as <c>title</c>).</summary>
    public ContextBuilder<T> Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ConfiguredLabel = label;
        return this;
    }

    /// <summary>A longer explanation of the context.</summary>
    public ContextBuilder<T> Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ConfiguredDescription = description;
        return this;
    }

    /// <summary>
    /// How the event refers to the context: a subject property (<c>o =&gt; o.CustomerId</c>) or, for a composite
    /// reference, an anonymous object of properties (<c>o =&gt; new { o.Address.Country, o.Address.Postcode }</c>). The
    /// engine resolves it from the event and passes it to the context provider. Calling it again replaces it.
    /// </summary>
    public ContextBuilder<T> ReferencedBy<TReference>(Expression<Func<T, TReference>> reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        PropertyChains.FromReference(reference);
        Reference = reference;
        return this;
    }
}

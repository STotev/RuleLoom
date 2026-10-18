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
/// Describes a context: returned by <see cref="EventSchema{T}"/>'s <c>Context(...)</c>.
/// </summary>
public sealed class ContextBuilder
{
    internal ContextBuilder(string name, Type type, FieldConfigurations fields)
    {
        Name = name;
        Type = type;
        Fields = fields;
    }

    internal string Name { get; }
    internal Type Type { get; }
    internal FieldConfigurations Fields { get; }
    internal string? ConfiguredLabel { get; private set; }
    internal string? ConfiguredDescription { get; private set; }
    internal bool IsMany { get; private set; }

    /// <summary>A readable name for authoring UIs (written as <c>title</c>).</summary>
    public ContextBuilder Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ConfiguredLabel = label;
        return this;
    }

    /// <summary>A longer explanation of the context.</summary>
    public ContextBuilder Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ConfiguredDescription = description;
        return this;
    }

    /// <summary>Marks the context as a collection of its type (e.g. an ad's watchers).</summary>
    public ContextBuilder Many()
    {
        IsMany = true;
        return this;
    }
}

/// <summary>
/// Configures a context's fields: passed to <c>Context(name, configure)</c>.
/// </summary>
/// <typeparam name="TContext">The context's type.</typeparam>
public sealed class ContextFields<TContext>
{
    internal ContextFields(FieldConfigurations fields) => Fields = fields;

    internal FieldConfigurations Fields { get; }

    /// <summary>Configures a field of the context, at any depth (<c>c =&gt; c.Address.City</c>).</summary>
    public FieldBuilder Field<TProperty>(Expression<Func<TContext, TProperty>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return Fields.Field(property);
    }
}

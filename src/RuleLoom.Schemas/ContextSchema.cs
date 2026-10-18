using System.Linq.Expressions;
using System.Text.Json;

namespace RuleLoom.Schemas;

/// <summary>
/// A context schema that can be built; implemented by <see cref="ContextSchema{T}"/>.
/// </summary>
public interface IContextSchema
{
    /// <summary>
    /// Generates the context's schema.
    /// </summary>
    /// <param name="options">The serializer options the context is produced with; <see cref="JsonSerializerDefaults.Web"/>
    /// when omitted.</param>
    ContextSchemaDefinition Build(JsonSerializerOptions? options = null);
}

/// <summary>
/// Base class for describing the fields of a context: the entity a context provider loads. Configure it in the
/// constructor:
/// <code>
/// public sealed class AccountSchema : ContextSchema&lt;AccountDto&gt;
/// {
///     public AccountSchema()
///     {
///         Context("account").Label("Account");
///         Field(a =&gt; a.Balance).Label("Balance");
///         Field(a =&gt; a.InternalRiskScore).Ignore();
///     }
/// }
/// </code>
/// Every event that relates to the context (<c>Context("toAccount").LoadedBy("account")</c>) shares this description.
/// </summary>
/// <typeparam name="T">The type the provider loads (one element of a collection context).</typeparam>
public abstract class ContextSchema<T> : IContextSchema
{
    private readonly FieldConfigurations _fields = new();
    private ContextSchemaBuilder? _context;

    /// <summary>
    /// Names the context provider this schema describes (required).
    /// </summary>
    /// <exception cref="InvalidOperationException">The context is already named.</exception>
    protected ContextSchemaBuilder Context(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (_context is not null)
            throw new InvalidOperationException($"The context is already named '{_context.Name}'.");

        return _context = new ContextSchemaBuilder(providerName);
    }

    /// <summary>
    /// Configures a field of the context, at any depth (<c>a =&gt; a.Owner.Email</c>).
    /// </summary>
    protected FieldBuilder Field<TProperty>(Expression<Func<T, TProperty>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return _fields.Field(property);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The context wasn't named, or a configured field isn't part of the
    /// serialized form.</exception>
    public ContextSchemaDefinition Build(JsonSerializerOptions? options = null)
    {
        if (_context is null)
            throw new InvalidOperationException($"{GetType().Name} doesn't name its context; call Context(\"...\") in its constructor.");

        return SchemaGenerator.GenerateContext(_context, typeof(T), _fields, options);
    }
}

/// <summary>
/// Describes a context schema: returned by <see cref="ContextSchema{T}"/>'s <c>Context(...)</c>.
/// </summary>
public sealed class ContextSchemaBuilder
{
    internal ContextSchemaBuilder(string name) => Name = name;

    internal string Name { get; }
    internal string? ConfiguredLabel { get; private set; }
    internal string? ConfiguredDescription { get; private set; }
    internal bool IsMany { get; private set; }

    /// <summary>A readable name for authoring UIs (written as <c>title</c>).</summary>
    public ContextSchemaBuilder Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ConfiguredLabel = label;
        return this;
    }

    /// <summary>A longer explanation of the context.</summary>
    public ContextSchemaBuilder Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ConfiguredDescription = description;
        return this;
    }

    /// <summary>Marks the context as a collection: the provider returns many of the schema's type (e.g. an ad's watchers).</summary>
    public ContextSchemaBuilder Many()
    {
        IsMany = true;
        return this;
    }
}

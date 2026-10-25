using System.Collections;
using System.Linq.Expressions;
using System.Text.Json;

namespace RuleLoom.Schemas;

/// <summary>
/// Describes the data a context provider loads, configured in the constructor:
/// <code>
/// public sealed class AccountSchema : ContextSchema&lt;AccountDto&gt;
/// {
///     public AccountSchema()
///     {
///         Label("Account");
///         Field(a =&gt; a.Balance).Label("Balance");
///         Field(a =&gt; a.InternalRiskScore).Ignore();
///     }
/// }
/// </code>
/// Fields are generated from how <typeparamref name="T"/> is serialized; only configured labels are written. Every
/// provider of <typeparamref name="T"/>, and every event that relates to its contexts
/// (<c>Context("toAccount").LoadedBy("account")</c>), shares this schema.
/// </summary>
/// <typeparam name="T">The type a provider loads (one item of a collection context).</typeparam>
public abstract class ContextSchema<T> : IContextSchema
{
    private readonly FieldConfigurations _fields = new();
    private string? _label;
    private string? _description;

    /// <inheritdoc />
    public Type DataType => typeof(T);

    /// <summary>A readable name for authoring UIs (written as <c>title</c>).</summary>
    protected void Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _label = label;
    }

    /// <summary>A longer explanation of the context.</summary>
    protected void Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _description = description;
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
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> doesn't serialize as a JSON object (e.g. a
    /// collection type for a single context), or a configured field isn't part of the serialized form.</exception>
    public ContextSchemaDefinition Build(string name, bool isMany = false, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (IsCollection(typeof(T)))
        {
            throw new InvalidOperationException(
                $"Context '{name}' is described by a collection type ({typeof(T).Name}); describe one item instead " +
                "(a collection context provider of the item type).");
        }

        return SchemaGenerator.GenerateContext(name, _label, _description, isMany, typeof(T), _fields, options);
    }

    private static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) &&
        !type.GetInterfaces().Append(type).Any(i => i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
}

/// <summary>
/// A context schema that can be built; implemented by <see cref="ContextSchema{T}"/>.
/// </summary>
public interface IContextSchema
{
    /// <summary>The type it describes, which a provider loads.</summary>
    Type DataType { get; }

    /// <summary>
    /// Generates the context's schema under the name its provider is registered with.
    /// </summary>
    /// <param name="name">The name the provider is registered under (e.g. <c>account</c>).</param>
    /// <param name="isMany">Whether the provider loads a collection of the type.</param>
    /// <param name="options">The serializer options the context is described with; <see cref="JsonSerializerDefaults.Web"/>
    /// when omitted.</param>
    ContextSchemaDefinition Build(string name, bool isMany = false, JsonSerializerOptions? options = null);
}

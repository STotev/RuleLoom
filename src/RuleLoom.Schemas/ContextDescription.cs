using System.Collections;
using System.Linq.Expressions;
using System.Text.Json;

namespace RuleLoom.Schemas;

/// <summary>
/// Describes the context a provider loads: given to the provider's <c>Describe</c> method, where labels, descriptions
/// and ignored fields are configured:
/// <code>
/// protected override void Describe(ContextDescription&lt;AccountDto&gt; context)
/// {
///     context.Label("Account");
///     context.Field(a =&gt; a.Balance).Label("Balance");
///     context.Field(a =&gt; a.InternalRiskScore).Ignore();
/// }
/// </code>
/// Fields are generated from how <typeparamref name="T"/> is serialized; only configured labels are written. Every event
/// that relates to the context (<c>Context("toAccount").LoadedBy("account")</c>) shares this description.
/// </summary>
/// <typeparam name="T">The type the provider loads (one item of a collection context).</typeparam>
public sealed class ContextDescription<T>
{
    private readonly FieldConfigurations _fields = new();
    private string? _label;
    private string? _description;

    /// <summary>A readable name for authoring UIs (written as <c>title</c>).</summary>
    public ContextDescription<T> Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _label = label;
        return this;
    }

    /// <summary>A longer explanation of the context.</summary>
    public ContextDescription<T> Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _description = description;
        return this;
    }

    /// <summary>
    /// Configures a field of the context, at any depth (<c>a =&gt; a.Owner.Email</c>).
    /// </summary>
    public FieldBuilder Field<TProperty>(Expression<Func<T, TProperty>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return _fields.Field(property);
    }

    /// <summary>
    /// Generates the context's schema; context providers call it with the name they're registered under.
    /// </summary>
    /// <param name="name">The name the provider is registered under (e.g. <c>account</c>).</param>
    /// <param name="isMany">Whether the provider loads a collection of <typeparamref name="T"/>.</param>
    /// <param name="options">The serializer options the context is described with; <see cref="JsonSerializerDefaults.Web"/>
    /// when omitted.</param>
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

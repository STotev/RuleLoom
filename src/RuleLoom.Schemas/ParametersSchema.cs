using System.Linq.Expressions;

namespace RuleLoom.Schemas;

/// <summary>
/// Describes the parameters type of an action: its fields come from how <typeparamref name="T"/> is read with
/// <see cref="ParametersSchemaDefinition.JsonOptions"/>, and <see cref="Parameter{TProperty}"/> adds labels and
/// descriptions or leaves parameters out. Usually configured through an action descriptor's <c>Parameter(...)</c>.
/// </summary>
/// <typeparam name="T">The action's parameters type.</typeparam>
public sealed class ParametersSchema<T>
{
    private readonly FieldConfigurations _fields = new();

    /// <summary>
    /// Configures a parameter, or a field of one at any depth (<c>p =&gt; p.Recipient.Email</c>).
    /// </summary>
    public FieldBuilder Parameter<TProperty>(Expression<Func<T, TProperty>> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return _fields.Field(parameter);
    }

    /// <summary>Generates the parameters' schema.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> doesn't serialize as a JSON object, or a
    /// configured parameter isn't part of the serialized form.</exception>
    public ParametersSchemaDefinition Build() => SchemaGenerator.GenerateParameters(typeof(T), _fields);
}

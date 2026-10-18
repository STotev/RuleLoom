using System.Linq.Expressions;
using System.Reflection;

namespace RuleLoom.Schemas;

/// <summary>
/// Configuration of the fields of one type, keyed by property chain (<c>o =&gt; o.Customer.Tier</c>).
/// </summary>
internal sealed class FieldConfigurations
{
    private readonly Dictionary<string, (IReadOnlyList<PropertyInfo> Properties, FieldConfiguration Configuration)> _fields = [];

    public IEnumerable<(IReadOnlyList<PropertyInfo> Properties, FieldConfiguration Configuration)> All => _fields.Values;

    public FieldBuilder Field(LambdaExpression property)
    {
        var properties = PropertyChains.FromProperty(property);
        var key = PropertyChains.Describe(properties);

        if (!_fields.TryGetValue(key, out var entry))
            _fields[key] = entry = (properties, new FieldConfiguration());

        return new FieldBuilder(entry.Configuration);
    }

}

/// <summary>
/// Reads property chains (<c>o =&gt; o.Customer.Tier</c>) and composite references
/// (<c>o =&gt; new { o.Address.Country, o.Address.Postcode }</c>) from expressions.
/// </summary>
internal static class PropertyChains
{
    public static string Describe(IReadOnlyList<PropertyInfo> properties) => string.Join('.', properties.Select(p => p.Name));

    public static IReadOnlyList<PropertyInfo> FromProperty(LambdaExpression lambda) =>
        Chain(lambda.Body, lambda.Parameters[0])
        ?? throw new ArgumentException($"'{lambda}' must be a chain of properties, e.g. o => o.Customer.Tier.", nameof(lambda));

    /// <summary>
    /// A single chain (name <c>null</c>) or the named chains of an anonymous object.
    /// </summary>
    public static IReadOnlyList<(string? Name, IReadOnlyList<PropertyInfo> Properties)> FromReference(LambdaExpression lambda)
    {
        if (Chain(lambda.Body, lambda.Parameters[0]) is { } single)
            return [(null, single)];

        if (lambda.Body is NewExpression { Members: { Count: > 0 } members } created &&
            created.Arguments.Select(argument => Chain(argument, lambda.Parameters[0])).ToList() is var chains &&
            chains.All(chain => chain is not null))
        {
            return members.Select((member, i) => ((string?)member.Name, chains[i]!)).ToList();
        }

        throw new ArgumentException(
            $"'{lambda}' must be a property (o => o.CustomerId) or an anonymous object of properties " +
            "(o => new { o.Address.Country, o.Address.Postcode }).", nameof(lambda));
    }

    private static IReadOnlyList<PropertyInfo>? Chain(Expression body, ParameterExpression parameter)
    {
        var properties = new List<PropertyInfo>();
        body = body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : body;

        while (body is MemberExpression { Member: PropertyInfo property } member)
        {
            properties.Insert(0, property);
            body = member.Expression!;
        }

        return body == parameter && properties.Count > 0 ? properties : null;
    }
}

internal sealed class FieldConfiguration
{
    public string? Label { get; set; }
    public string? Description { get; set; }
    public bool Ignored { get; set; }
}

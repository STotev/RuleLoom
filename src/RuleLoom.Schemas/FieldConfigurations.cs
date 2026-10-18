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
        var properties = PropertyChain(property);
        var key = string.Join('.', properties.Select(p => p.Name));

        if (!_fields.TryGetValue(key, out var entry))
            _fields[key] = entry = (properties, new FieldConfiguration());

        return new FieldBuilder(entry.Configuration);
    }

    private static IReadOnlyList<PropertyInfo> PropertyChain(LambdaExpression lambda)
    {
        var properties = new List<PropertyInfo>();
        var body = lambda.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : lambda.Body;

        while (body is MemberExpression { Member: PropertyInfo property } member)
        {
            properties.Insert(0, property);
            body = member.Expression;
        }

        if (body != lambda.Parameters[0] || properties.Count == 0)
            throw new ArgumentException($"'{lambda}' must be a chain of properties, e.g. o => o.Customer.Tier.", nameof(lambda));

        return properties;
    }
}

internal sealed class FieldConfiguration
{
    public string? Label { get; set; }
    public string? Description { get; set; }
    public bool Ignored { get; set; }
}

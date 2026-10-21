using System.Collections;
using System.Globalization;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Values;
using RuleLoom.Operators;
using RuleLoom.Schemas;

namespace RuleLoom.Validation;

/// <summary>
/// Checks a rule's paths against its event's schema and the schemas of the contexts it uses, and flags comparisons of
/// built-in operators whose operand types can't match. Problems that need no schema (reserved prefixes, unknown
/// variables, contexts after a state) are left to <see cref="RuleValidator.Validate"/>.
/// </summary>
/// <param name="event">The schema of the rule's event.</param>
/// <param name="contexts">The event's contexts the rule uses, by context name (ignoring case): their schema, or
/// <c>null</c> when the provider describes none. Declared contexts without a registered provider are missing.</param>
/// <param name="operators">The registered operators, by name.</param>
internal sealed class SchemaPathChecker(
    EventSchemaDefinition @event,
    IReadOnlyDictionary<string, ContextSchemaDefinition?> contexts,
    IReadOnlyDictionary<string, IConditionOperator> operators)
{
    private static readonly HashSet<Type> ComparisonOperators =
    [
        typeof(EqualsOperator), typeof(GreaterThanOperator), typeof(GreaterThanOrEqualOperator),
        typeof(LessThanOperator), typeof(LessThanOrEqualOperator)
    ];

    private static readonly HashSet<Type> OrderingOperators =
        [typeof(GreaterThanOperator), typeof(GreaterThanOrEqualOperator), typeof(LessThanOperator), typeof(LessThanOrEqualOperator)];

    private readonly List<RuleValidationError> _errors = [];
    private readonly List<RuleValidationError> _warnings = [];

    public (IReadOnlyList<RuleValidationError> Errors, IReadOnlyList<RuleValidationError> Warnings) Check(RuleDefinition rule)
    {
        CheckCondition(rule.Condition, "$.condition");

        for (var index = 0; index < rule.Actions.Count; index++)
        {
            var action = rule.Actions[index];
            CheckCondition(action.Condition, $"$.actions[{index}].condition");

            if (action.Schedule?.ReferencePath is { } referencePath)
                Resolve(referencePath, $"$.actions[{index}].schedule.referencePath");
        }

        return (_errors, _warnings);
    }

    /// <returns>The schema field a path operand points at; <c>null</c> for other nodes and unchecked paths.</returns>
    private FieldDefinition? CheckCondition(ConditionNode? node, string location)
    {
        switch (node)
        {
            case PathOperand operand:
                return Resolve(operand.Path, $"{location}.path");
            case OperatorCondition condition:
                var fields = condition.Operands
                    .Select((operand, index) => CheckCondition(operand, $"{location}.operands[{index}]"))
                    .ToList();
                CheckComparison(condition, fields, location);
                return null;
            default:
                return null;
        }
    }

    private void CheckComparison(OperatorCondition condition, IReadOnlyList<FieldDefinition?> fields, string location)
    {
        if (!operators.TryGetValue(condition.Operator, out var @operator) || !ComparisonOperators.Contains(@operator.GetType()) ||
            condition.Operands is not [var left, var right])
        {
            return;
        }

        var (leftField, rightField) = (fields[0], fields[1]);

        // One warning per comparison: values that can't be ordered first, then types that can't match.
        var problem = (leftField, rightField, left, right) switch
        {
            _ when OrderingOperators.Contains(@operator.GetType()) &&
                   new[] { leftField, rightField }.FirstOrDefault(f => f?.Type is FieldType.Object or FieldType.Array) is { } unordered =>
                $"can't order {Describe(unordered.Type)}",
            ({ } l, { } r, _, _) => Compatible(l, r) ? null : $"compares {Describe(l.Type)} with {Describe(r.Type)}",
            ({ } l, null, _, ValueOperand value) => Compatible(l, value.Value) ? null : $"compares {Describe(l.Type)} with {Show(value.Value)}",
            (null, { } r, ValueOperand value, _) => Compatible(r, value.Value) ? null : $"compares {Show(value.Value)} with {Describe(r.Type)}",
            _ => null
        };

        if (problem is not null)
            _warnings.Add(new(location, $"'{condition.Operator}' {problem}; the condition is never true."));
    }

    /// <summary>Resolves <paramref name="path"/> on the schemas, reporting problems at <paramref name="location"/>.</summary>
    /// <returns>The field, or <c>null</c> when the path is wrong or can't be checked.</returns>
    private FieldDefinition? Resolve(string path, string location)
    {
        var segments = path.Split('.');
        var first = segments[0];
        IReadOnlyList<FieldDefinition> fields;
        string owner;

        if (first.StartsWith('@') && first.Length > 1)
        {
            var name = first[1..];
            if (!contexts.TryGetValue(name, out var context))
            {
                var declared = @event.Contexts.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                return Report(location, declared is null
                    ? $"'{first}' is not a context of {@event.EventType}."
                    : $"No context provider is registered under '{declared.Provider}' (context '{first}').");
            }

            if (context is null)
            {
                _warnings.Add(new(location, $"Context '{first}' has no schema; paths into it aren't checked."));
                return null;
            }

            if (segments.Length == 1)
                return null;

            if (context.IsMany)
                return Report(location, $"'{first}' is a collection; paths can't go into its items ('{path}').");

            (fields, owner, segments) = (context.Fields, $"'{first}'", segments[1..]);
        }
        else if (first.Equals("$current", StringComparison.OrdinalIgnoreCase) || first.Equals("$previous", StringComparison.OrdinalIgnoreCase))
        {
            // A context after a state is reported by Validate.
            if (segments.Length == 1 || segments[1].StartsWith('@'))
                return null;

            (fields, owner, segments) = (@event.Fields, @event.EventType, segments[1..]);
        }
        else if (first.StartsWith('$') || first.StartsWith('#') || first.StartsWith('@'))
        {
            return null;   // reported by Validate
        }
        else
        {
            (fields, owner) = (@event.Fields, @event.EventType);
        }

        for (var index = 0; ; index++)
        {
            if (Find(fields, segments[index]) is not { } field)
                return Report(location, $"'{segments[index]}' is not a field of {owner}.");

            if (index == segments.Length - 1)
                return field;

            var prefix = $"'{string.Join('.', segments[..(index + 1)])}'";
            switch (field.Type)
            {
                case FieldType.Object when field.Fields.Count > 0:
                    (fields, owner) = (field.Fields, prefix);
                    break;
                case FieldType.Object or FieldType.Any:
                    return null;   // untyped, a dictionary or a repeated type: no declared fields to check
                case FieldType.Array:
                    return Report(location, $"{prefix} is a list; paths can't go into its items ('{path}').");
                default:
                    return Report(location, $"{prefix} is {Describe(field.Type)} and has no fields ('{path}').");
            }
        }
    }

    private FieldDefinition? Report(string location, string message)
    {
        _errors.Add(new(location, message));
        return null;
    }

    /// <summary>Like property paths: an exact-case match wins; an ambiguous case-insensitive match is unknown.</summary>
    private static FieldDefinition? Find(IReadOnlyList<FieldDefinition> fields, string name)
    {
        if (fields.FirstOrDefault(f => f.Name == name) is { } exact)
            return exact;

        var matches = fields.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Whether a value can match a field's values under <see cref="ValueComparer"/>. Enums aren't judged: CLR enums match
    /// by C# name or number, JSON state by the serialized text, and the schema can't tell which the rule runs on.
    /// </summary>
    private static bool Compatible(FieldDefinition field, object? value) => value is null || field.Type switch
    {
        FieldType.Integer or FieldType.Number => IsNumber(value) || value is Enum,
        FieldType.Boolean => value is bool,
        FieldType.String => value is string or DateTime or DateTimeOffset or Enum,
        FieldType.DateTime or FieldType.Date => value is DateTime or DateTimeOffset ||
                                                value is string text && ValueConverter.TryConvert<DateTimeOffset>(text, out _),
        _ => true
    };

    private static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool Compatible(FieldDefinition left, FieldDefinition right) =>
        Kind(left.Type) is not { } l || Kind(right.Type) is not { } r || l == r;

    // Field types that compare with each other; null when not checked (text can hold dates, numbers as text or enum
    // names; objects and lists compare by content).
    private static string? Kind(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.Number => "number",
        FieldType.Boolean => "boolean",
        FieldType.DateTime or FieldType.Date => "date",
        _ => null
    };

    private static string Describe(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.Number => "a number",
        FieldType.String => "text",
        FieldType.Boolean => "a boolean",
        FieldType.DateTime => "a date and time",
        FieldType.Date => "a date",
        FieldType.Enum => "an enum",
        FieldType.Object => "an object",
        FieldType.Array => "a list",
        _ => "any value"
    };

    private static string Show(object? value) => value switch
    {
        string text => $"the text '{text}'",
        bool flag => flag ? "true" : "false",
        IDictionary => "an object",
        IEnumerable => "a list",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => $"{value}"
    };
}

using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Values;
using RuleLoom.Operators;
using RuleLoom.Schemas;

namespace RuleLoom.Validation;

/// <summary>
/// Checks a rule's paths against its event's schema and the schemas of the contexts it uses, and flags comparisons of
/// built-in operators whose operand types can't match.
/// </summary>
/// <param name="event">The schema of the rule's event.</param>
/// <param name="contexts">The event's contexts the rule uses, by context name: their schema, or <c>null</c> when the
/// provider describes none. Declared contexts without a registered provider are missing.</param>
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

    private void CheckCondition(ConditionNode? node, string location)
    {
        switch (node)
        {
            case PathOperand operand:
                Resolve(operand.Path, $"{location}.path");
                break;
            case OperatorCondition condition:
                for (var index = 0; index < condition.Operands.Count; index++)
                    CheckCondition(condition.Operands[index], $"{location}.operands[{index}]");
                CheckOperator(condition, location);
                break;
        }
    }

    private void CheckOperator(OperatorCondition condition, string location)
    {
        if (!operators.TryGetValue(condition.Operator, out var @operator))
            return;

        var type = @operator.GetType();

        if (type == typeof(ChangedOperator))
        {
            foreach (var (operand, index) in condition.Operands.Select((operand, index) => (operand, index)))
            {
                if (operand is PathOperand { Path: var path } && path.StartsWith('@'))
                    _errors.Add(new($"{location}.operands[{index}].path", $"'changed' can't be used on a context ('{path}'): contexts have no previous state."));
            }
        }

        if (!ComparisonOperators.Contains(type) || condition.Operands is not [var left, var right])
            return;

        var leftField = Field(left);
        var rightField = Field(right);

        if (OrderingOperators.Contains(type))
        {
            foreach (var field in new[] { leftField, rightField })
            {
                if (field?.Type is FieldType.Boolean or FieldType.Object or FieldType.Array)
                    _warnings.Add(new(location, $"'{condition.Operator}' can't order {Describe(field.Type)}; the condition is never true."));
            }
        }

        var mismatch = (leftField, rightField, left, right) switch
        {
            ({ } l, { } r, _, _) => Compatible(l, r) ? null : $"compares {Describe(l.Type)} with {Describe(r.Type)}",
            ({ } l, null, _, ValueOperand value) => Compatible(l, value.Value) ? null : $"compares {Describe(l.Type)} with {Show(value.Value)}",
            (null, { } r, ValueOperand value, _) => Compatible(r, value.Value) ? null : $"compares {Show(value.Value)} with {Describe(r.Type)}",
            _ => null
        };

        if (mismatch is not null)
            _warnings.Add(new(location, $"'{condition.Operator}' {mismatch}; the condition is never true."));
    }

    /// <summary>The schema field a path operand points at, or <c>null</c> for values and unchecked paths.</summary>
    private FieldDefinition? Field(ConditionNode operand) =>
        operand is PathOperand { Path: var path } ? Resolve(path, location: null) : null;

    /// <summary>
    /// Resolves <paramref name="path"/> on the schemas, reporting problems at <paramref name="location"/> (none when
    /// <c>null</c>: the path was already reported).
    /// </summary>
    /// <returns>The field, or <c>null</c> when the path is wrong or can't be checked.</returns>
    private FieldDefinition? Resolve(string path, string? location)
    {
        var segments = path.Split('.');
        var first = segments[0];
        IReadOnlyList<FieldDefinition> fields;
        string owner;

        if (first.StartsWith('@'))
        {
            var name = first[1..];
            if (contexts.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) is not { } declared)
            {
                var problem = @event.Contexts.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    ? $"No context provider is registered under '{Provider(name)}' (context '{first}')."
                    : $"'{first}' is not a context of {@event.EventType}.";
                return Report(location, problem);
            }

            if (contexts[declared] is not { } context)
            {
                if (location is not null)
                    _warnings.Add(new(location, $"Context '{first}' has no schema; paths into it aren't checked."));
                return null;
            }

            if (segments.Length == 1)
                return null;

            if (context.IsMany)
                return Report(location, $"'{first}' is a collection; paths can't go into its items ('{path}').");

            (fields, owner, segments) = (context.Fields, $"'{first}'", segments[1..]);
        }
        else if (first.StartsWith('$'))
        {
            if (!first.Equals("$current", StringComparison.OrdinalIgnoreCase) && !first.Equals("$previous", StringComparison.OrdinalIgnoreCase))
                return Report(location, $"Unknown variable '{first}' in path '{path}'. Use $current or $previous.");

            if (segments.Length > 1 && segments[1].StartsWith('@'))
                return Report(location, $"Contexts have no current or previous state; use '{string.Join('.', segments[1..])}' instead of '{path}'.");

            if (segments.Length == 1)
                return null;

            (fields, owner, segments) = (@event.Fields, @event.EventType, segments[1..]);
        }
        else if (first.StartsWith('#'))
        {
            return Report(location, $"Paths starting with '#' are reserved for functions: '{path}'.");
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

    private FieldDefinition? Report(string? location, string message)
    {
        if (location is not null)
            _errors.Add(new(location, message));
        return null;
    }

    private string Provider(string context) =>
        @event.Contexts.First(c => string.Equals(c.Name, context, StringComparison.OrdinalIgnoreCase)).Provider;

    /// <summary>Like property paths: an exact-case match wins; an ambiguous case-insensitive match is unknown.</summary>
    private static FieldDefinition? Find(IReadOnlyList<FieldDefinition> fields, string name)
    {
        if (fields.FirstOrDefault(f => f.Name == name) is { } exact)
            return exact;

        var matches = fields.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool Compatible(FieldDefinition field, object? value) => value is null || (field.Type, value) switch
    {
        (FieldType.Integer or FieldType.Number, _) => IsNumber(value),
        (FieldType.Boolean, _) => value is bool,
        (FieldType.String, _) => value is string or Guid,
        (FieldType.DateTime or FieldType.Date, string text) => ValueConverter.TryConvert<DateTimeOffset>(text, out _),
        (FieldType.DateTime or FieldType.Date, _) => value is DateTime or DateTimeOffset or DateOnly,
        (FieldType.Enum, string text) => field.EnumValues.Contains(text, StringComparer.OrdinalIgnoreCase),
        (FieldType.Enum, _) => value is Enum || IsNumber(value),
        _ => true
    };

    private static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool Compatible(FieldDefinition left, FieldDefinition right) => (Kind(left.Type), Kind(right.Type)) switch
    {
        (null, _) or (_, null) => true,
        var (l, r) => l == r
    };

    // Field types that compare with each other; null when not checked.
    private static string? Kind(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.Number => "number",
        FieldType.Boolean => "boolean",
        FieldType.DateTime or FieldType.Date => "date",
        _ => null   // text can hold dates, numbers as text or enum names; objects and lists aren't checked
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
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => $"{value}"
    };
}

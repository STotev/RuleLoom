using System.Globalization;
using System.Text.Json;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Values;
using RuleLoom.Schemas;

namespace RuleLoom.Validation;

/// <summary>
/// A typed action's parameters must match its parameters schema: no unknown parameters, every required one, values of
/// their fields' types (errors); a path whose field can't be read as its parameter is a warning. Actions without a
/// parameters schema aren't checked.
/// </summary>
internal sealed class ParameterCheck(Catalog catalog, SchemaPaths? schemaPaths) : RuleCheck
{
    public override void Parameters(ActionDefinition action, IReadOnlyDictionary<string, PathNode> paths, string location,
        ValidationProblems problems)
    {
        if (string.IsNullOrWhiteSpace(action.Action) || !catalog.TryGetAction(action.Action, out var registration) ||
            registration.Parameters is not { } schema)
            return;

        var matched = Match(action.Parameters.Keys, schema.Fields, "parameter", $"Action '{action.Action}'", $"{location}.parameters", problems);

        foreach (var (name, field) in matched)
        {
            if (paths.TryGetValue(name, out var path))
                Path(path, name, field, problems);
            else
                Value(action.Parameters[name], field, name, $"{location}.parameters.{name}", problems);
        }
    }

    /// <summary>
    /// Reports names that aren't fields and required fields that aren't named (both ignoring case, as binding does);
    /// returns the named fields.
    /// </summary>
    private static List<(string Name, FieldDefinition Field)> Match(IEnumerable<string> names, IReadOnlyList<FieldDefinition> fields,
        string member, string owner, string location, ValidationProblems problems)
    {
        List<(string Name, FieldDefinition Field)> matched = [];

        foreach (var name in names)
        {
            if (FieldDefinition.Find(fields, name) is { } field)
                matched.Add((name, field));
            else
                problems.Error($"{location}.{name}", $"{owner} has no {member} '{name}'.");
        }

        foreach (var field in fields.Where(f => f.IsRequired))
        {
            if (!matched.Any(m => string.Equals(m.Field.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                problems.Error(location, $"{owner} requires the {member} '{field.Name}'.");
        }

        return matched;
    }

    private void Path(PathNode path, string name, FieldDefinition parameter, ValidationProblems problems)
    {
        if (schemaPaths?.Resolve(path, problems) is { } field && !CanRead(parameter.Type, field.Type))
        {
            problems.Warning(path.Location,
                $"'{name}' is {FieldTypes.Describe(parameter.Type)}, but '{path.Text}' is {FieldTypes.Describe(field.Type)}; the action can't read it.");
        }
    }

    // Whether a value of a field's type can be bound as a parameter of another type; JSON state may still convert.
    private static bool CanRead(FieldType parameter, FieldType field) => (parameter, field) switch
    {
        (FieldType.Any, _) or (_, FieldType.Any) => true,
        (FieldType.Integer or FieldType.Number, FieldType.Integer or FieldType.Number) => true,
        (FieldType.String, FieldType.String or FieldType.DateTime or FieldType.Date or FieldType.Enum) => true,
        (FieldType.DateTime or FieldType.Date, FieldType.DateTime or FieldType.Date or FieldType.String) => true,
        (FieldType.Enum, FieldType.Enum or FieldType.String or FieldType.Integer) => true,
        _ => parameter == field
    };

    /// <summary>Checks a value as the JSON it is bound from.</summary>
    private static void Value(object? value, FieldDefinition field, string name, string location, ValidationProblems problems)
    {
        JsonElement json;
        try
        {
            json = JsonSerializer.SerializeToElement(value, ParametersSchemaDefinition.JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            problems.Error(location, $"'{name}' can't be written as JSON: {ex.Message}");
            return;
        }

        Value(json, field, name, location, problems);
    }

    private static void Value(JsonElement value, FieldDefinition field, string name, string location, ValidationProblems problems)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!field.IsNullable)
                problems.Error(location, $"'{name}' can't be null.");
            return;
        }

        if (!Fits(value, field))
        {
            problems.Error(location, $"'{name}' must be {Expected(field)}, not {Show(value)}.");
            return;
        }

        if (field is { Type: FieldType.Object, Fields.Count: > 0 })
        {
            var properties = value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            foreach (var (member, nested) in Match(properties.Keys, field.Fields, "field", $"'{name}'", location, problems))
                Value(properties[member], nested, $"{name}.{member}", $"{location}.{member}", problems);
        }
        else if (field is { Type: FieldType.Array, Items: { } items })
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                Value(item, items, $"{name}[{index}]", $"{location}[{index}]", problems);
                index++;
            }
        }
    }

    private static bool Fits(JsonElement value, FieldDefinition field) => field.Type switch
    {
        FieldType.String => value.ValueKind == JsonValueKind.String,
        FieldType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        FieldType.Number => value.ValueKind == JsonValueKind.Number,
        FieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        FieldType.DateTime => value.ValueKind == JsonValueKind.String && ValueConverter.TryConvert<DateTimeOffset>(value.GetString(), out _),
        FieldType.Date => value.ValueKind == JsonValueKind.String &&
                          DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        FieldType.Enum => value.ValueKind == JsonValueKind.String &&
                          field.EnumValues.Contains(value.GetString()!, StringComparer.OrdinalIgnoreCase),
        FieldType.Object => value.ValueKind == JsonValueKind.Object,
        FieldType.Array => value.ValueKind == JsonValueKind.Array,
        _ => true
    };

    private static string Expected(FieldDefinition field) => field.Type switch
    {
        FieldType.Integer => "a whole number",
        FieldType.DateTime => "a date and time (ISO 8601)",
        FieldType.Date => "a date (yyyy-MM-dd)",
        FieldType.Enum => $"one of {string.Join(", ", field.EnumValues)}",
        var type => FieldTypes.Describe(type)
    };

    private static string Show(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"the text '{value.GetString()}'",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        _ => value.GetRawText()
    };
}

using System.Text.Json;
using System.Text.Json.Serialization;
using RuleLoom.Abstractions.Condition;

namespace RuleLoom.Abstractions.Rule;

/// <summary>
/// Performant JSON to RuleDefinition parser using System.Text.Json.
/// Uses a polymorphic converter for ConditionNode types to handle path, value, and operator conditions.
/// </summary>
public static class RuleJsonParser
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();

    /// <summary>
    /// Parses a JSON string into a RuleDefinition.
    /// </summary>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="RuleParseException">The JSON doesn't describe a valid rule.</exception>
    public static RuleDefinition Parse(string json)
    {
        return ToRule(JsonSerializer.Deserialize<JsonElement>(json, DefaultOptions), "$");
    }

    /// <summary>
    /// Parses a JSON string into a RuleDefinition asynchronously.
    /// </summary>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="RuleParseException">The JSON doesn't describe a valid rule.</exception>
    public static async Task<RuleDefinition> ParseAsync(Stream jsonStream)
    {
        return ToRule(await JsonSerializer.DeserializeAsync<JsonElement>(jsonStream, DefaultOptions), "$");
    }

    /// <summary>
    /// Parses multiple rules from a JSON array.
    /// </summary>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="RuleParseException">The JSON isn't an array of valid rules; the path names the failing rule's index.</exception>
    public static List<RuleDefinition> ParseMany(string json)
    {
        var document = JsonSerializer.Deserialize<JsonElement>(json, DefaultOptions);

        if (document.ValueKind != JsonValueKind.Array)
            throw new RuleParseException("Expected a JSON array of rules.", "$");

        return document.EnumerateArray().Select((rule, index) => ToRule(rule, $"$[{index}]")).ToList();
    }

    /// <summary>
    /// Parses a JSON array of rules rule by rule: a malformed rule is reported in its result and the others are still
    /// parsed.
    /// </summary>
    /// <returns>One result per element of the array, in order.</returns>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="RuleParseException">The JSON isn't an array.</exception>
    public static IReadOnlyList<RuleParseResult> ParseEach(string json)
    {
        var document = JsonSerializer.Deserialize<JsonElement>(json, DefaultOptions);

        if (document.ValueKind != JsonValueKind.Array)
            throw new RuleParseException("Expected a JSON array of rules.", "$");

        return document.EnumerateArray().Select((rule, index) =>
        {
            try
            {
                return new RuleParseResult(index, ToRule(rule, $"$[{index}]"), null);
            }
            catch (RuleParseException ex)
            {
                return new RuleParseResult(index, null, ex);
            }
        }).ToList();
    }

    private static RuleDefinition ToRule(JsonElement rule, string path)
    {
        RuleJsonValidator.ValidateRule(rule, path);

        try
        {
            return rule.Deserialize<RuleDefinition>(DefaultOptions)
                ?? throw new JsonException("Failed to deserialize rule definition");
        }
        catch (JsonException ex) when (ex is not RuleParseException)
        {
            // A value of the wrong type the shape check doesn't cover; its path is relative to the rule.
            var message = ex.Message.Split(" Path: ")[0];
            throw new RuleParseException(message, ex.Path is { } relative ? path + relative[1..] : path);
        }
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new ConditionNodeJsonConverter(),
                new ParametersJsonConverter(),
                new ClrValueJsonConverter(),
                new JsonStringEnumConverter()
            }
        };
        return options;
    }
}

/// <summary>
/// Polymorphic JSON converter for ConditionNode types.
/// Determines the type based on the presence of "operator", "path", or "value" properties.
/// </summary>
internal class ConditionNodeJsonConverter : JsonConverter<ConditionNode>
{
    public override ConditionNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token for ConditionNode");

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Determine the type based on properties present (case-insensitive)
        if (HasProperty(root, "operator"))
        {
            // OperatorCondition: has "operator" property
            return ParseOperatorCondition(root);
        }

        if (HasProperty(root, "path"))
        {
            // PathOperand: has "path" property
            return ParsePathOperand(root);
        }

        if (HasProperty(root, "value") || !root.EnumerateObject().Any())
        {
            // ValueOperand: has "value" property or is empty (null value)
            return ParseValueOperand(root);
        }

        throw new JsonException("Unknown ConditionNode type. Expected 'operator', 'path', or 'value' property.");
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static JsonElement GetPropertyCaseInsensitive(JsonElement element, string propertyName)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }
        throw new JsonException($"Property '{propertyName}' not found");
    }

    public override void Write(Utf8JsonWriter writer, ConditionNode value, JsonSerializerOptions options)
    {
        // Serialize based on the actual type
        switch (value)
        {
            case PathOperand pathOperand:
                JsonSerializer.Serialize(writer, pathOperand, options);
                break;
            case ValueOperand valueOperand:
                JsonSerializer.Serialize(writer, valueOperand, options);
                break;
            case OperatorCondition operatorCondition:
                JsonSerializer.Serialize(writer, operatorCondition, options);
                break;
            default:
                throw new JsonException($"Unknown ConditionNode type: {value.GetType().Name}");
        }
    }

    private static PathOperand ParsePathOperand(JsonElement element)
    {
        var path = GetPropertyCaseInsensitive(element, "path").GetString()
            ?? throw new JsonException("PathOperand 'path' property is required");

        return new PathOperand { Path = path };
    }

    private static ValueOperand ParseValueOperand(JsonElement element)
    {
        object? value = null;

        if (HasProperty(element, "value"))
        {
            var valueProp = GetPropertyCaseInsensitive(element, "value");
            value = JsonClrValues.ToClrValue(valueProp);
        }

        return new ValueOperand { Value = value };
    }

    private static OperatorCondition ParseOperatorCondition(JsonElement element)
    {
        var operatorName = GetPropertyCaseInsensitive(element, "operator").GetString()
            ?? throw new JsonException("OperatorCondition 'operator' property is required");

        var operands = new List<ConditionNode>();

        if (HasProperty(element, "operands"))
        {
            var operandsProp = GetPropertyCaseInsensitive(element, "operands");
            if (operandsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var operandElement in operandsProp.EnumerateArray())
                {
                    var operand = ParseConditionNode(operandElement);
                    operands.Add(operand);
                }
            }
        }

        return new OperatorCondition
        {
            Operator = operatorName,
            Operands = operands
        };
    }

    private static ConditionNode ParseConditionNode(JsonElement element)
    {
        if (HasProperty(element, "operator"))
            return ParseOperatorCondition(element);
        if (HasProperty(element, "path"))
            return ParsePathOperand(element);
        if (HasProperty(element, "value") || element.ValueKind == JsonValueKind.Object && !element.EnumerateObject().Any())
            return ParseValueOperand(element);

        throw new JsonException("Unknown ConditionNode type in operands");
    }
}

/// <summary>
/// Converts JSON values to plain CLR values, so rule values look the same whether a rule came from JSON or code:
/// <c>null</c>, <see cref="bool"/>, <see cref="int"/>/<see cref="long"/>/<see cref="double"/>, <see cref="string"/>,
/// <see cref="List{T}"/> of values, and <see cref="Dictionary{TKey,TValue}"/> of values.
/// </summary>
internal static class JsonClrValues
{
    public static object? ToClrValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var intVal) => intVal,
            JsonValueKind.Number when element.TryGetInt64(out var longVal) => longVal,
            JsonValueKind.Number when element.TryGetDouble(out var doubleVal) => doubleVal,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => element.EnumerateArray().Select(ToClrValue).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ToClrValue(p.Value)),
            _ => element.GetRawText()
        };
    }
}

/// <summary>
/// Reads action parameters: a <c>{ "$path": ... }</c> object (checked by <see cref="RuleJsonValidator"/>) as a
/// <see cref="RulePath"/>, anything else as a plain CLR value; writes paths back as <c>{ "$path": ... }</c>.
/// </summary>
internal sealed class ParametersJsonConverter : JsonConverter<Dictionary<string, object?>>
{
    public override Dictionary<string, object?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);

        return document.RootElement.EnumerateObject().ToDictionary(
            parameter => parameter.Name,
            parameter => RuleJsonValidator.IsPath(parameter.Value, out var path)
                ? RulePath.Parse(path.GetString()!)
                : JsonClrValues.ToClrValue(parameter.Value));
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object?> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (name, parameter) in value)
        {
            writer.WritePropertyName(name);

            if (parameter is RulePath path)
            {
                writer.WriteStartObject();
                writer.WriteString(RuleJsonValidator.PathKey, path.ToString());
                writer.WriteEndObject();
            }
            else
            {
                JsonSerializer.Serialize(writer, parameter, options);
            }
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Reads <see cref="object"/>-typed values (such as action parameters) as plain CLR values instead of
/// <see cref="JsonElement"/>; writes them by their runtime type.
/// </summary>
internal sealed class ClrValueJsonConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return JsonClrValues.ToClrValue(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value.GetType() == typeof(object))
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Abstractions.Rule;

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
    public static RuleDefinition Parse(string json)
    {
        return JsonSerializer.Deserialize<RuleDefinition>(json, DefaultOptions)
            ?? throw new JsonException("Failed to deserialize rule definition");
    }

    /// <summary>
    /// Parses a JSON string into a RuleDefinition asynchronously.
    /// </summary>
    public static async Task<RuleDefinition> ParseAsync(Stream jsonStream)
    {
        return await JsonSerializer.DeserializeAsync<RuleDefinition>(jsonStream, DefaultOptions)
            ?? throw new JsonException("Failed to deserialize rule definition");
    }

    /// <summary>
    /// Parses multiple rules from a JSON array.
    /// </summary>
    public static List<RuleDefinition> ParseMany(string json)
    {
        return JsonSerializer.Deserialize<List<RuleDefinition>>(json, DefaultOptions)
            ?? throw new JsonException("Failed to deserialize rules");
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
                new ConditionNodeJsonConverter()
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
            value = ParseJsonValue(valueProp);
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

    /// <summary>
    /// Converts a JsonElement to a CLR object, preserving type information where possible.
    /// </summary>
    private static object? ParseJsonValue(JsonElement element)
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
            JsonValueKind.Array => element.EnumerateArray().Select(ParseJsonValue).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ParseJsonValue(p.Value)),
            _ => element.GetRawText()
        };
    }
}




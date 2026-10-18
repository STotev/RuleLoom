using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// Reads the subset of JSON Schema that <see cref="SchemaGenerator"/> writes into the model.
/// </summary>
internal static class SchemaReader
{
    public static (string EventType, string? Label, string? Description, IReadOnlyList<FieldDefinition> Fields,
        IReadOnlyList<ContextDefinition> Contexts) ReadEvent(JsonObject document)
    {
        var eventType = String(document, SchemaGenerator.EventKeyword)
            ?? throw new JsonException($"An event schema needs '{SchemaGenerator.EventKeyword}'.");

        var contexts = document[SchemaGenerator.ContextsKeyword] switch
        {
            null => [],
            JsonObject nodes => nodes.Select(pair => ReadContext(pair.Key, Object(pair.Value, pair.Key))).ToList(),
            _ => throw new JsonException($"'{SchemaGenerator.ContextsKeyword}' must be an object.")
        };

        return (eventType, String(document, "title"), String(document, "description"),
            ReadFields(document), contexts);
    }

    private static ContextDefinition ReadContext(string name, JsonObject node)
    {
        var isMany = Types(node).Contains("array");
        var element = isMany ? Object(node["items"], $"{name}.items") : node;

        return new ContextDefinition(name, String(node, "title"), String(node, "description"),
            isMany, ReadFields(element));
    }

    private static List<FieldDefinition> ReadFields(JsonObject schema) => schema["properties"] switch
    {
        null => [],
        JsonObject properties => properties.Select(pair => ReadField(pair.Key, Object(pair.Value, pair.Key))).ToList(),
        _ => throw new JsonException("'properties' must be an object.")
    };

    private static FieldDefinition ReadField(string name, JsonObject node)
    {
        var types = Types(node);
        var isNullable = types.Contains("null");
        var enumValues = node["enum"] is JsonArray values
            ? values.Where(v => v is not null).Select(v => v!.ToString()).ToList()
            : [];

        var type = enumValues.Count > 0 ? FieldType.Enum : ReadType(types, String(node, "format"), name);

        return new FieldDefinition(
            name,
            type,
            isNullable,
            String(node, "title"),
            String(node, "description"),
            enumValues,
            type == FieldType.Object ? ReadFields(node) : [],
            type == FieldType.Array && node["items"] is JsonObject items ? ReadField(string.Empty, items) : null);
    }

    /// <summary>
    /// The first non-null type wins, except that numbers win over strings (lenient number handling writes both).
    /// No type (e.g. a <c>$ref</c> to a repeated type) is an object without fields.
    /// </summary>
    private static FieldType ReadType(IReadOnlyList<string> types, string? format, string name)
    {
        var candidates = types.Where(t => t != "null").ToList();

        if (candidates.Contains("integer"))
            return FieldType.Integer;
        if (candidates.Contains("number"))
            return FieldType.Number;

        return candidates.FirstOrDefault() switch
        {
            null or "object" => FieldType.Object,
            "string" => format switch
            {
                "date-time" => FieldType.DateTime,
                "date" => FieldType.Date,
                _ => FieldType.String
            },
            "boolean" => FieldType.Boolean,
            "array" => FieldType.Array,
            var other => throw new JsonException($"Field '{name}' has an unknown type '{other}'.")
        };
    }

    private static List<string> Types(JsonObject node) => node["type"] switch
    {
        JsonValue value => [value.GetValue<string>()],
        JsonArray array => array.Where(t => t is not null).Select(t => t!.GetValue<string>()).ToList(),
        _ => []
    };

    private static string? String(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject Object(JsonNode? node, string name) =>
        node as JsonObject ?? throw new JsonException($"'{name}' must be a JSON object.");
}

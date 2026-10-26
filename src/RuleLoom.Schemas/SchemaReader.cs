using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// Reads the subset of JSON Schema that <see cref="SchemaGenerator"/> writes into the model.
/// </summary>
internal static class SchemaReader
{
    /// <summary>The document's name: its event type or context name under <paramref name="keyword"/>.</summary>
    public static string ReadName(JsonObject document, string keyword, string what) =>
        String(document, keyword) is { } name && !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new JsonException($"{what} needs a non-empty '{keyword}'.");

    public static IReadOnlyList<ContextDefinition> ReadContexts(JsonObject document)
    {
        var contexts = document[SchemaGenerator.ContextsKeyword] switch
        {
            null => [],
            JsonObject nodes => nodes.Select(pair => ReadContext(pair.Key, Object(pair.Value, pair.Key))).ToList(),
            _ => throw new JsonException($"'{SchemaGenerator.ContextsKeyword}' must be an object.")
        };

        if (contexts.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
            throw new JsonException($"Context '{duplicate.Key}' appears more than once (names ignore case).");

        return contexts;
    }

    /// <summary>The schema of one item of a context: the document itself, or its <c>items</c> for a collection.</summary>
    public static (bool IsMany, JsonObject Item) ReadItem(JsonObject document, string name) =>
        Types(document).Contains("array")
            ? (true, document["items"] as JsonObject ?? throw new JsonException($"Context schema '{name}' is an array without object 'items'."))
            : (false, document);

    private static ContextDefinition ReadContext(string name, JsonObject node)
    {
        var reference = node[SchemaGenerator.ReferencedByKeyword] switch
        {
            null => null,
            JsonValue path when path.TryGetValue<string>(out var text) => new ContextReference(text, new Dictionary<string, string>()),
            JsonObject parts => new ContextReference(null, parts.ToDictionary(
                part => part.Key,
                part => part.Value is JsonValue value && value.TryGetValue<string>(out var text)
                    ? text
                    : throw new JsonException($"Part '{part.Key}' of the reference of context '{name}' must be a path."))),
            _ => throw new JsonException($"The reference of context '{name}' must be a path or an object of paths.")
        };

        // Without a provider (documents written before it was always written), the context's name, written into the
        // document so it names every provider.
        node[SchemaGenerator.ProviderKeyword] ??= name;

        var provider = String(node, SchemaGenerator.ProviderKeyword) is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : throw new JsonException($"The provider of context '{name}' must be a non-empty string.");

        return new ContextDefinition(name, String(node, "title"), String(node, "description"), reference, provider);
    }

    public static List<FieldDefinition> ReadFields(JsonObject schema, JsonObject root)
    {
        var required = schema["required"] switch
        {
            null => [],
            JsonArray names => names.Select(n => n is JsonValue value && value.TryGetValue<string>(out var name)
                ? name
                : throw new JsonException("'required' must be an array of names.")).ToHashSet(),
            _ => throw new JsonException("'required' must be an array of names.")
        };

        return schema["properties"] switch
        {
            null => [],
            JsonObject properties => properties.Select(pair => ReadField(pair.Key, pair.Value, required.Contains(pair.Key), root)).ToList(),
            _ => throw new JsonException("'properties' must be an object.")
        };
    }

    private static FieldDefinition ReadField(string name, JsonNode? node, bool isRequired, JsonObject root)
    {
        // The schema `true` (any value): what the exporter writes for object, JsonElement and JsonNode.
        if (node is JsonValue any && any.TryGetValue<bool>(out var accepted) && accepted)
            return new FieldDefinition(name, FieldType.Any, true, isRequired, null, null, [], [], null);

        if (node is not JsonObject field)
            throw new JsonException($"'{name}' must be a JSON object or true.");

        // A remaining $ref points at an ancestor (a repeated type): describe it by its target, without its fields.
        var isCycle = field["$ref"] is not null;
        var described = isCycle ? Target(field, root, name) : field;

        var types = Types(described);
        var enumValues = described["enum"] is JsonArray values ? values.Where(v => v is not null).Select(v => v!.ToString()).ToList() : [];
        var type = enumValues.Count > 0 ? FieldType.Enum : ReadType(types, String(described, "format"), name);
        var isNullable = type == FieldType.Any || types.Contains("null") || described["enum"] is JsonArray all && all.Any(v => v is null);

        return new FieldDefinition(
            name,
            type,
            isNullable,
            isRequired,
            String(field, "title"),
            String(field, "description"),
            enumValues,
            type == FieldType.Object && !isCycle ? ReadFields(described, root) : [],
            type == FieldType.Array && !isCycle ? ReadField(string.Empty, described["items"] ?? JsonValue.Create(true), false, root) : null);
    }

    private static JsonObject Target(JsonObject field, JsonObject root, string name) =>
        field["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var pointer) &&
        SchemaGenerator.Resolve(root, pointer) is JsonObject target
            ? target
            : throw new JsonException($"Field '{name}' has a '$ref' that doesn't point inside the document.");

    /// <summary>
    /// The first non-null type wins. A number that may also be a string is sent as a string (WriteAsString); numbers
    /// win otherwise. No type (the schema <c>{}</c>) is any value.
    /// </summary>
    private static FieldType ReadType(IReadOnlyList<string> types, string? format, string name)
    {
        var candidates = types.Where(t => t != "null").ToList();
        var isNumber = candidates.Contains("integer") || candidates.Contains("number");

        if (isNumber && candidates.Contains("string"))
            return FieldType.String;
        if (candidates.Contains("integer"))
            return FieldType.Integer;
        if (candidates.Contains("number"))
            return FieldType.Number;

        return candidates.FirstOrDefault() switch
        {
            null => FieldType.Any,
            "object" => FieldType.Object,
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

    // Without "type", the types of the "anyOf" branches (e.g. a number that may also be NaN or Infinity).
    private static List<string> Types(JsonObject node) => node["type"] switch
    {
        null when node["anyOf"] is JsonArray branches => branches.OfType<JsonObject>().SelectMany(Types).Distinct().ToList(),
        null => [],
        JsonValue value => [TypeName(value)],
        JsonArray array => array.Select(TypeName).ToList(),
        _ => throw new JsonException("'type' must be a string or an array of strings.")
    };

    private static string TypeName(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var name) ? name : throw new JsonException("'type' must be a string or an array of strings.");

    public static string? String(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject Object(JsonNode? node, string name) =>
        node as JsonObject ?? throw new JsonException($"'{name}' must be a JSON object.");
}

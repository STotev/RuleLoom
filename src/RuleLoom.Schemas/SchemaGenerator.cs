using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RuleLoom.Schemas;

/// <summary>
/// Generates event documents with System.Text.Json's schema exporter and applies RuleLoom's layer: event keywords,
/// configured titles and descriptions, ignored fields and contexts.
/// </summary>
internal static class SchemaGenerator
{
    public const string SchemaUri = "https://json-schema.org/draft/2020-12/schema";
    public const string EventKeyword = "x-ruleloom-event";
    public const string ContextsKeyword = "x-ruleloom-contexts";

    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    public static EventSchemaDefinition Generate(
        EventBuilder @event,
        Type subjectType,
        FieldConfigurations fields,
        IReadOnlyList<ContextBuilder> contexts,
        JsonSerializerOptions? options)
    {
        var serializer = PrepareOptions(options ?? WebDefaults);

        var document = new JsonObject
        {
            ["$schema"] = SchemaUri,
            [EventKeyword] = @event.EventType
        };

        if (@event.ConfiguredLabel is { } label)
            document["title"] = label;
        if (@event.ConfiguredDescription is { } description)
            document["description"] = description;

        MoveMembers(GenerateType(subjectType, fields, serializer, subjectType.Name), document);

        if (contexts.Count > 0)
        {
            var contextNodes = new JsonObject();

            foreach (var context in contexts)
                contextNodes[context.Name] = GenerateContext(context, serializer);

            document[ContextsKeyword] = contextNodes;
        }

        return new EventSchemaDefinition(document);
    }

    private static JsonObject GenerateContext(ContextBuilder context, JsonSerializerOptions serializer)
    {
        var node = new JsonObject();

        if (context.ConfiguredLabel is { } label)
            node["title"] = label;
        if (context.ConfiguredDescription is { } description)
            node["description"] = description;

        var schema = GenerateType(context.Type, context.Fields, serializer, $"context '{context.Name}'");
        var pointer = $"#/{ContextsKeyword}/{EscapePointer(context.Name)}";

        if (context.IsMany)
        {
            RewriteReferences(schema, pointer + "/items");
            node["type"] = "array";
            node["items"] = schema;
        }
        else
        {
            RewriteReferences(schema, pointer);
            MoveMembers(schema, node);
        }

        return node;
    }

    private static JsonObject GenerateType(Type type, FieldConfigurations fields, JsonSerializerOptions serializer, string owner)
    {
        var schema = serializer.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true })
            as JsonObject ?? throw new InvalidOperationException($"{type.Name} can't be described as a JSON object.");

        if (!IsObject(schema))
            throw new InvalidOperationException($"{type.Name} ({owner}) must serialize as a JSON object.");

        schema["type"] = "object";

        foreach (var (properties, configuration) in fields.All)
            Apply(schema, JsonPath(type, properties, serializer, owner), configuration, properties, owner);

        return schema;
    }

    private static void Apply(JsonObject schema, IReadOnlyList<string> path, FieldConfiguration configuration,
        IReadOnlyList<PropertyInfo> properties, string owner)
    {
        var parent = schema;
        JsonObject? field = null;

        for (var i = 0; i < path.Count; i++)
        {
            var reason = parent switch
            {
                _ when parent["$ref"] is not null => "it's below a repeated type",
                _ when parent["properties"] is not JsonObject => "it's inside a dictionary or an object without fields",
                _ when parent["properties"]![path[i]] is not JsonObject => $"'{path[i]}' isn't serialized, e.g. [JsonIgnore]",
                _ => null
            };

            if (reason is not null)
            {
                throw new InvalidOperationException(
                    $"Field '{string.Join('.', properties.Select(p => p.Name))}' of {owner} has no place in the schema: {reason}.");
            }

            field = (JsonObject)parent["properties"]![path[i]]!;
            if (i < path.Count - 1)
                parent = field;
        }

        if (configuration.Ignored)
        {
            ((JsonObject)parent["properties"]!).Remove(path[^1]);
            if (parent["required"] is JsonArray required)
                required.Remove(required.FirstOrDefault(name => name?.GetValue<string>() == path[^1]));
            return;
        }

        if (configuration.Label is { } label)
            field!["title"] = label;
        if (configuration.Description is { } description)
            field!["description"] = description;
    }

    /// <summary>
    /// Maps a property chain to JSON property names through the serializer's contract (naming policy, renames).
    /// </summary>
    private static IReadOnlyList<string> JsonPath(Type type, IReadOnlyList<PropertyInfo> properties, JsonSerializerOptions serializer, string owner)
    {
        var path = new List<string>();
        var current = type;

        foreach (var property in properties)
        {
            var member = serializer.GetTypeInfo(current).Properties
                .FirstOrDefault(p => p.AttributeProvider is PropertyInfo info && info.Name == property.Name);

            if (member is null)
            {
                throw new InvalidOperationException(
                    $"Field '{string.Join('.', properties.Select(p => p.Name))}' of {owner} isn't serialized " +
                    $"('{property.Name}' isn't written, e.g. [JsonIgnore]).");
            }

            path.Add(member.Name);
            current = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
        }

        return path;
    }

    private static void RewriteReferences(JsonNode? node, string prefix)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["$ref"] is JsonValue reference && reference.GetValue<string>() is var target && target.StartsWith('#'))
                    obj["$ref"] = prefix + target[1..];

                foreach (var (_, value) in obj)
                    RewriteReferences(value, prefix);
                break;
            case JsonArray array:
                foreach (var item in array)
                    RewriteReferences(item, prefix);
                break;
        }
    }

    private static bool IsObject(JsonObject schema) => schema["type"] switch
    {
        JsonValue value => value.GetValue<string>() == "object",
        JsonArray types => types.Any(t => t?.GetValue<string>() == "object"),
        _ => schema["properties"] is not null
    };

    private static void MoveMembers(JsonObject from, JsonObject to)
    {
        foreach (var name in from.Select(p => p.Key).ToList())
        {
            var value = from[name];
            from.Remove(name);
            to[name] = value;
        }
    }

    private static string EscapePointer(string name) => name.Replace("~", "~0").Replace("/", "~1");

    /// <summary>
    /// Describes what is written: numbers aren't typed as strings (reading leniency), and the exporter needs a resolver.
    /// </summary>
    private static JsonSerializerOptions PrepareOptions(JsonSerializerOptions options)
    {
        var prepared = new JsonSerializerOptions(options);
        prepared.NumberHandling &= ~JsonNumberHandling.AllowReadingFromString;
        prepared.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        prepared.MakeReadOnly();
        return prepared;
    }
}

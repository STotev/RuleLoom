using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RuleLoom.Schemas;

/// <summary>
/// Generates event documents with System.Text.Json's schema exporter and applies RuleLoom's layer: event keywords,
/// configured titles and descriptions, ignored fields and the contexts the event relates to.
/// </summary>
internal static class SchemaGenerator
{
    public const string SchemaUri = "https://json-schema.org/draft/2020-12/schema";
    public const string EventKeyword = "x-ruleloom-event";
    public const string ContextsKeyword = "x-ruleloom-contexts";
    public const string ReferencedByKeyword = "x-ruleloom-referenced-by";

    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    // Prepared once per options instance, so System.Text.Json's metadata cache is shared by every schema built with them.
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> Prepared = new();

    public static EventSchemaDefinition Generate(
        EventBuilder @event,
        Type subjectType,
        FieldConfigurations fields,
        IReadOnlyList<ContextDeclaration> contexts,
        JsonSerializerOptions? options)
    {
        var serializer = Prepared.GetValue(options ?? WebDefaults, PrepareOptions);

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
                contextNodes[context.Name] = DescribeContext(context, subjectType, serializer);

            document[ContextsKeyword] = contextNodes;
        }

        return new EventSchemaDefinition(document);
    }

    private static JsonObject DescribeContext(ContextDeclaration context, Type subjectType, JsonSerializerOptions serializer)
    {
        var node = new JsonObject();

        if (context.Label is { } label)
            node["title"] = label;
        if (context.Description is { } description)
            node["description"] = description;

        if (context.Reference is { } reference)
        {
            var owner = $"the reference of context '{context.Name}'";
            var parts = PropertyChains.FromReference(reference)
                .Select(part => (part.Name, Path: string.Join('.', JsonPath(subjectType, part.Properties, serializer, owner))))
                .ToList();

            node[ReferencedByKeyword] = parts is [(null, var path)]
                ? path
                : new JsonObject(parts.Select(part => KeyValuePair.Create(
                    serializer.PropertyNamingPolicy?.ConvertName(part.Name!) ?? part.Name!, (JsonNode?)part.Path)));
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
        InlineSiblingReferences(schema);

        var configured = fields.All
            .Select(entry => (Path: JsonPath(type, entry.Properties, serializer, owner), entry.Properties, entry.Configuration))
            .ToList();

        var ignored = configured.Where(entry => entry.Configuration.Ignored).ToList();
        foreach (var entry in configured)
        {
            var parent = ignored.FirstOrDefault(i => i.Path.Count < entry.Path.Count && i.Path.SequenceEqual(entry.Path.Take(i.Path.Count)));
            if (parent.Path is not null)
            {
                throw new InvalidOperationException(
                    $"Field '{Describe(entry.Properties)}' of {owner} is inside ignored field '{Describe(parent.Properties)}'.");
            }
        }

        // Labels and descriptions first, then removals, so the order of configuration calls doesn't matter.
        foreach (var entry in configured.OrderBy(entry => entry.Configuration.Ignored))
            Apply(schema, entry.Path, entry.Configuration, entry.Properties, owner);

        return schema;
    }

    private static string Describe(IReadOnlyList<PropertyInfo> properties) => string.Join('.', properties.Select(p => p.Name));

    /// <summary>
    /// The exporter writes a <c>$ref</c> for every repeat of a type, also to a sibling property. Copies those targets in
    /// place, so the only references left point at an ancestor (a real cycle): removing an ignored field can't leave a
    /// reference to it, and readers only meet references for cycles.
    /// </summary>
    private static void InlineSiblingReferences(JsonObject root)
    {
        var copies = 0;
        Visit(root, "#");

        void Visit(JsonNode? node, string pointer)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var target) &&
                        !IsAncestor(target, pointer) && Resolve(root, target) is JsonObject resolved)
                    {
                        if (++copies > 256)
                            throw new InvalidOperationException("The type's schema has too many repeated parts to inline.");

                        var copy = (JsonObject)resolved.DeepClone();
                        Rebase(copy, target, pointer);
                        obj.Remove("$ref");
                        foreach (var (name, value) in copy.ToList())
                        {
                            copy.Remove(name);
                            obj[name] ??= value;
                        }
                    }

                    foreach (var (name, value) in obj.ToList())
                        Visit(value, $"{pointer}/{name.Replace("~", "~0").Replace("/", "~1")}");
                    break;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                        Visit(array[i], $"{pointer}/{i}");
                    break;
            }
        }

        static bool IsAncestor(string target, string pointer) => pointer == target || pointer.StartsWith(target + "/", StringComparison.Ordinal);

        // References inside the copied part that pointed into it now point into the copy.
        static void Rebase(JsonNode? node, string from, string to)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var target) && IsAncestor(from, target))
                        obj["$ref"] = to + target[from.Length..];
                    foreach (var (_, child) in obj)
                        Rebase(child, from, to);
                    break;
                case JsonArray array:
                    foreach (var child in array)
                        Rebase(child, from, to);
                    break;
            }
        }
    }

    internal static JsonNode? Resolve(JsonNode root, string pointer)
    {
        if (!pointer.StartsWith('#'))
            return null;

        JsonNode? node = root;
        foreach (var segment in pointer[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
            node = node is JsonObject obj ? obj[segment.Replace("~1", "/").Replace("~0", "~")] : null;

        return node;
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
                _ when parent["properties"]![path[i]] is null => $"'{path[i]}' isn't serialized, e.g. [JsonIgnore]",
                _ => null
            };

            if (reason is not null)
            {
                throw new InvalidOperationException(
                    $"Field '{string.Join('.', properties.Select(p => p.Name))}' of {owner} has no place in the schema: {reason}.");
            }

            // An untyped property (object, JsonElement) is the schema `true`: make it an object to describe it.
            if (parent["properties"]![path[i]] is not JsonObject member)
                parent["properties"]![path[i]] = member = new JsonObject();

            field = member;
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
            // Properties the serializer never writes ([JsonIgnore]) are listed without a getter.
            var member = serializer.GetTypeInfo(current).Properties
                .FirstOrDefault(p => p.AttributeProvider is PropertyInfo info && info.Name == property.Name && p.Get is not null);

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

/// <summary>
/// A context declared by an event schema: its name, label, description and reference expression.
/// </summary>
internal sealed record ContextDeclaration(string Name, string? Label, string? Description, LambdaExpression? Reference);

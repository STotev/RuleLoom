using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// An event's schema: its type, the fields of its subject and its contexts. Built by <see cref="EventSchema{T}"/> or
/// read from JSON; written as a JSON Schema 2020-12 document with RuleLoom keywords.
/// </summary>
public sealed class EventSchemaDefinition
{
    // Indented, readable JSON (no escaping of characters such as '+'); shared by every writer of schema documents.
    internal static readonly JsonWriterOptions WriterOptions = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = WriterOptions.Encoder };

    private readonly JsonObject _document;

    internal EventSchemaDefinition(JsonObject document)
    {
        _document = document;
        (EventType, Label, Description, Fields, Contexts) = SchemaReader.ReadEvent(document);
    }

    /// <summary>The event type rules are written for.</summary>
    public string EventType { get; }

    /// <summary>A readable name for authoring UIs, if configured (<c>title</c>).</summary>
    public string? Label { get; }

    /// <summary>An optional longer explanation.</summary>
    public string? Description { get; }

    /// <summary>The fields of the event's subject.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>The contexts the event's rules can use with <c>@name</c> paths.</summary>
    public IReadOnlyList<ContextDefinition> Contexts { get; }

    /// <summary>
    /// Writes the schema as a JSON Schema 2020-12 document.
    /// </summary>
    public string ToJson() => _document.ToJsonString(WriteOptions);

    /// <summary>
    /// Reads an event schema written by <see cref="ToJson"/>.
    /// </summary>
    /// <exception cref="JsonException">The JSON isn't an event schema.</exception>
    public static EventSchemaDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return Read(() => JsonNode.Parse(json) is JsonObject document
            ? new EventSchemaDefinition(document)
            : throw new JsonException("An event schema must be a JSON object."));
    }

    /// <summary>
    /// Reads a JSON array of event schemas, as written by <see cref="EventSchemaCollection.ToJson"/>.
    /// </summary>
    /// <exception cref="JsonException">The JSON isn't an array of event schemas.</exception>
    public static IReadOnlyList<EventSchemaDefinition> ParseMany(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return Read(() =>
        {
            if (JsonNode.Parse(json) is not JsonArray documents)
                throw new JsonException("Expected a JSON array of event schemas.");

            var items = documents.ToList();
            documents.Clear();   // detaches the items, so they can be kept without copying

            return items
                .Select((node, index) => node is JsonObject document
                    ? new EventSchemaDefinition(document)
                    : throw new JsonException($"Item {index} isn't an event schema."))
                .ToList();
        });
    }

    // JsonNode reports some malformed documents (duplicate keys, values of the wrong kind) with other exceptions.
    private static T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            throw new JsonException($"The JSON isn't a valid event schema: {ex.Message}", ex);
        }
    }

    internal void WriteTo(Utf8JsonWriter writer) => _document.WriteTo(writer);
}

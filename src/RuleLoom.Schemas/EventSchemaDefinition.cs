using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// An event's schema: its type, the fields of its subject and its contexts. Built by <see cref="EventSchema{T}"/> or
/// read from JSON; written as a JSON Schema 2020-12 document with RuleLoom keywords.
/// </summary>
public sealed class EventSchemaDefinition
{
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
    public string ToJson() => _document.ToJsonString(SchemaJson.WriteOptions);

    /// <summary>
    /// Reads an event schema written by <see cref="ToJson"/>.
    /// </summary>
    /// <exception cref="JsonException">The JSON isn't an event schema.</exception>
    public static EventSchemaDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return SchemaJson.Read(() => new EventSchemaDefinition(SchemaJson.ParseObject(json, "An event schema")));
    }

    internal void WriteTo(Utf8JsonWriter writer) => _document.WriteTo(writer);
}

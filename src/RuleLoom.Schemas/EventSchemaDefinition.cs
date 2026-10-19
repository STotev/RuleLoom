using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// An event's schema: its type, the fields of its subject and its contexts. Built by <see cref="EventSchema{T}"/> or
/// read from JSON; written as a JSON Schema 2020-12 document with RuleLoom keywords.
/// </summary>
public sealed class EventSchemaDefinition : SchemaDefinition
{
    internal EventSchemaDefinition(JsonObject document) : base(document)
    {
        EventType = SchemaReader.ReadName(document, SchemaGenerator.EventKeyword, "An event schema");
        Fields = SchemaReader.ReadFields(document, document);
        Contexts = SchemaReader.ReadContexts(document);
    }

    /// <summary>The event type rules are written for.</summary>
    public string EventType { get; }

    /// <summary>The contexts the event's rules can use with <c>@name</c> paths.</summary>
    public IReadOnlyList<ContextDefinition> Contexts { get; }

    /// <summary>
    /// Reads an event schema written by <see cref="SchemaDefinition.ToJson"/>.
    /// </summary>
    /// <exception cref="JsonException">The JSON isn't an event schema.</exception>
    public static EventSchemaDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return SchemaJson.Read(() => new EventSchemaDefinition(SchemaJson.ParseObject(json, "An event schema")));
    }
}

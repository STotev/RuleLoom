using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// Event and context schemas together: what a <see cref="SchemaCollection"/> builds, what is exported and imported as
/// one JSON array, and what is saved into an <see cref="ISchemaStore"/>.
/// </summary>
public sealed class SchemaSet
{
    /// <summary>Creates a set; event types and context names must be unique (ignoring case).</summary>
    /// <exception cref="InvalidOperationException">An event type or context name appears more than once.</exception>
    public SchemaSet(IEnumerable<EventSchemaDefinition> events, IEnumerable<ContextSchemaDefinition> contexts)
    {
        Events = events.ToList();
        Contexts = contexts.ToList();

        if (Events.GroupBy(e => e.EventType, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } eventType)
            throw new InvalidOperationException($"Event '{eventType.Key}' is described by more than one schema.");
        if (Contexts.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } context)
            throw new InvalidOperationException($"Context '{context.Key}' is described by more than one schema.");
    }

    /// <summary>The event schemas.</summary>
    public IReadOnlyList<EventSchemaDefinition> Events { get; }

    /// <summary>The context schemas.</summary>
    public IReadOnlyList<ContextSchemaDefinition> Contexts { get; }

    /// <summary>
    /// Writes the set as one JSON array of schema documents (events first, then contexts).
    /// </summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, SchemaJson.WriterOptions))
        {
            writer.WriteStartArray();
            foreach (var schema in Events.Concat<SchemaDefinition>(Contexts))
                schema.WriteTo(writer);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a JSON array of event and context schema documents, as written by <see cref="ToJson"/>.
    /// </summary>
    /// <exception cref="JsonException">The JSON isn't an array of schema documents, or names repeat.</exception>
    public static SchemaSet Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return SchemaJson.Read(() =>
        {
            if (JsonNode.Parse(json) is not JsonArray documents)
                throw new JsonException("Expected a JSON array of schema documents.");

            var items = documents.ToList();
            documents.Clear();   // detaches the items, so they can be kept without copying

            var events = new List<EventSchemaDefinition>();
            var contexts = new List<ContextSchemaDefinition>();

            foreach (var (node, index) in items.Select((node, index) => (node, index)))
            {
                switch (node)
                {
                    case JsonObject document when document.ContainsKey(SchemaGenerator.EventKeyword):
                        events.Add(new EventSchemaDefinition(document));
                        break;
                    case JsonObject document when document.ContainsKey(SchemaGenerator.ContextKeyword):
                        contexts.Add(new ContextSchemaDefinition(document));
                        break;
                    default:
                        throw new JsonException(
                            $"Item {index} isn't a schema document (needs '{SchemaGenerator.EventKeyword}' or '{SchemaGenerator.ContextKeyword}').");
                }
            }

            try
            {
                return new SchemaSet(events, contexts);
            }
            catch (InvalidOperationException ex)
            {
                throw new JsonException(ex.Message, ex);
            }
        });
    }
}

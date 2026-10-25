using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// A catalog written as one document: its event schemas, context schemas, operators, actions and problems. It is what a
/// catalog store keeps and what authoring UIs read. Every section is optional, so a document can hold only some of them
/// (e.g. the event schemas a producer defines).
/// </summary>
public sealed class CatalogDocument
{
    /// <summary>Creates a document from its entries.</summary>
    /// <exception cref="InvalidOperationException">Two entries of one kind have the same name (ignoring case).</exception>
    public CatalogDocument(
        IEnumerable<EventSchemaDefinition>? events = null,
        IEnumerable<ContextSchemaDefinition>? contexts = null,
        IEnumerable<CatalogOperator>? operators = null,
        IEnumerable<CatalogAction>? actions = null,
        IEnumerable<CatalogProblem>? problems = null)
    {
        Events = Distinct(events, e => e.EventType, "Event");
        Contexts = Distinct(contexts, c => c.Name, "Context");
        Operators = Distinct(operators, o => o.Name, "Operator");
        Actions = Distinct(actions, a => a.Name, "Action");
        Problems = problems?.ToList() ?? [];
    }

    /// <summary>The event schemas.</summary>
    public IReadOnlyList<EventSchemaDefinition> Events { get; }

    /// <summary>The context schemas, each named after the provider it describes.</summary>
    public IReadOnlyList<ContextSchemaDefinition> Contexts { get; }

    /// <summary>The operators rule conditions can use.</summary>
    public IReadOnlyList<CatalogOperator> Operators { get; }

    /// <summary>The actions rules can run.</summary>
    public IReadOnlyList<CatalogAction> Actions { get; }

    /// <summary>The broken links found in store-contributed entries when the catalog was built.</summary>
    public IReadOnlyList<CatalogProblem> Problems { get; }

    /// <summary>
    /// Writes the document as JSON: an object with <c>events</c> and <c>contexts</c> (JSON Schema documents),
    /// <c>operators</c>, <c>actions</c> and <c>problems</c>.
    /// </summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, SchemaJson.WriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("events");
            foreach (var schema in Events)
                schema.WriteTo(writer);
            writer.WriteEndArray();

            writer.WriteStartArray("contexts");
            foreach (var schema in Contexts)
                schema.WriteTo(writer);
            writer.WriteEndArray();

            writer.WriteStartArray("operators");
            foreach (var @operator in Operators)
            {
                writer.WriteStartObject();
                WriteNames(writer, @operator.Name, @operator.Label, @operator.Description);
                writer.WriteNumber("minOperands", @operator.MinOperands);
                if (@operator.MaxOperands is { } max)
                    writer.WriteNumber("maxOperands", max);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("actions");
            foreach (var action in Actions)
            {
                writer.WriteStartObject();
                WriteNames(writer, action.Name, action.Label, action.Description);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("problems");
            foreach (var problem in Problems)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", problem.Kind == CatalogEntryKind.Event ? "event" : "context");
                writer.WriteString("name", problem.Name);
                writer.WriteString("message", problem.Message);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a document written by <see cref="ToJson"/>. Missing sections are empty.
    /// </summary>
    /// <exception cref="JsonException">The JSON isn't a catalog document, or two entries of one kind have the same
    /// name.</exception>
    public static CatalogDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return SchemaJson.Read(() =>
        {
            var document = SchemaJson.ParseObject(json, "A catalog document");

            try
            {
                return new CatalogDocument(
                    Items(document, "events").Select(item => new EventSchemaDefinition(item)).ToList(),
                    Items(document, "contexts").Select(item => new ContextSchemaDefinition(item)).ToList(),
                    Items(document, "operators").Select(item => new CatalogOperator(
                        Name(item, "An operator"), SchemaReader.String(item, "label"), SchemaReader.String(item, "description"),
                        (int?)item["minOperands"] ?? 0, (int?)item["maxOperands"])).ToList(),
                    Items(document, "actions").Select(item => new CatalogAction(
                        Name(item, "An action"), SchemaReader.String(item, "label"), SchemaReader.String(item, "description"))).ToList(),
                    Items(document, "problems").Select(item => new CatalogProblem(
                        SchemaReader.String(item, "kind") switch
                        {
                            "event" => CatalogEntryKind.Event,
                            "context" => CatalogEntryKind.Context,
                            var kind => throw new JsonException($"A problem's kind must be 'event' or 'context', not '{kind}'.")
                        },
                        Name(item, "A problem"), SchemaReader.String(item, "message") ?? "")).ToList());
            }
            catch (InvalidOperationException ex)
            {
                throw new JsonException(ex.Message, ex);
            }
        });
    }

    private static List<T> Distinct<T>(IEnumerable<T>? items, Func<T, string> name, string kind)
    {
        var list = items?.ToList() ?? [];

        if (list.GroupBy(name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
            throw new InvalidOperationException($"{kind} '{duplicate.Key}' appears more than once in the catalog document.");

        return list;
    }

    // The section's objects, detached from the document so they can be kept without copying.
    private static List<JsonObject> Items(JsonObject document, string section)
    {
        switch (document[section])
        {
            case null:
                return [];
            case JsonArray array:
                var items = array.Select((node, index) => node as JsonObject
                    ?? throw new JsonException($"Item {index} of '{section}' must be a JSON object.")).ToList();
                array.Clear();
                return items;
            default:
                throw new JsonException($"'{section}' must be a JSON array.");
        }
    }

    private static string Name(JsonObject item, string what) =>
        SchemaReader.String(item, "name") is { Length: > 0 } name ? name : throw new JsonException($"{what} needs a 'name'.");

    private static void WriteNames(Utf8JsonWriter writer, string name, string? label, string? description)
    {
        writer.WriteString("name", name);
        if (label is not null)
            writer.WriteString("label", label);
        if (description is not null)
            writer.WriteString("description", description);
    }
}

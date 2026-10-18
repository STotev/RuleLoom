using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// A context's schema: the fields of the entity a context provider loads (e.g. <c>account</c>), shared by every event
/// that relates to it. Built by <see cref="ContextSchema{T}"/> or read from JSON; written as a JSON Schema 2020-12
/// document with <c>x-ruleloom-context</c>.
/// </summary>
public sealed class ContextSchemaDefinition
{
    private readonly JsonObject _document;

    internal ContextSchemaDefinition(JsonObject document)
    {
        _document = document;
        (Name, Label, Description, IsMany, Fields) = SchemaReader.ReadContextSchema(document);
    }

    /// <summary>The name of the context provider it describes (matched ignoring case).</summary>
    public string Name { get; }

    /// <summary>A readable name for authoring UIs, if configured (<c>title</c>).</summary>
    public string? Label { get; }

    /// <summary>An optional longer explanation.</summary>
    public string? Description { get; }

    /// <summary>Whether the provider returns a collection; <see cref="Fields"/> then describe one element.</summary>
    public bool IsMany { get; }

    /// <summary>The context's fields (of one element for collections).</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>Writes the schema as a JSON Schema 2020-12 document.</summary>
    public string ToJson() => _document.ToJsonString(SchemaJson.WriteOptions);

    /// <summary>Reads a context schema written by <see cref="ToJson"/>.</summary>
    /// <exception cref="JsonException">The JSON isn't a context schema.</exception>
    public static ContextSchemaDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return SchemaJson.Read(() => new ContextSchemaDefinition(SchemaJson.ParseObject(json, "A context schema")));
    }

    internal void WriteTo(Utf8JsonWriter writer) => _document.WriteTo(writer);
}

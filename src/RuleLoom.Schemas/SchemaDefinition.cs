using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// A schema document: an <see cref="EventSchemaDefinition"/>, a <see cref="ContextSchemaDefinition"/> or a
/// <see cref="ParametersSchemaDefinition"/>, written as a JSON Schema 2020-12 document with RuleLoom keywords.
/// </summary>
public abstract class SchemaDefinition
{
    private readonly JsonObject _document;

    private protected SchemaDefinition(JsonObject document)
    {
        _document = document;
        Label = SchemaReader.String(document, "title");
        Description = SchemaReader.String(document, "description");
    }

    /// <summary>A readable name for authoring UIs, if configured (<c>title</c>).</summary>
    public string? Label { get; }

    /// <summary>An optional longer explanation.</summary>
    public string? Description { get; }

    /// <summary>The described fields.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; private protected init; } = [];

    /// <summary>Writes the schema as a JSON Schema 2020-12 document.</summary>
    public string ToJson() => _document.ToJsonString(SchemaJson.WriteOptions);

    internal void WriteTo(Utf8JsonWriter writer) => _document.WriteTo(writer);
}

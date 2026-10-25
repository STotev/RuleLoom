using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// A context's schema: the fields of the entity a context provider loads (e.g. <c>account</c>), shared by every event
/// that relates to it. Built by <see cref="ContextSchema{T}"/> or read from JSON; written as
/// a JSON Schema 2020-12 document with <c>x-ruleloom-context</c>.
/// </summary>
public sealed class ContextSchemaDefinition : SchemaDefinition
{
    internal ContextSchemaDefinition(JsonObject document) : base(document)
    {
        Name = SchemaReader.ReadName(document, SchemaGenerator.ContextKeyword, "A context schema");
        (IsMany, var item) = SchemaReader.ReadItem(document, Name);
        Fields = SchemaReader.ReadFields(item, document);
    }

    /// <summary>The name the context provider it describes is registered under (matched ignoring case).</summary>
    public string Name { get; }

    /// <summary>Whether the provider returns a collection; <see cref="SchemaDefinition.Fields"/> then describe one item.</summary>
    public bool IsMany { get; }

    /// <summary>Reads a context schema written by <see cref="SchemaDefinition.ToJson"/>.</summary>
    /// <exception cref="JsonException">The JSON isn't a context schema.</exception>
    public static ContextSchemaDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return SchemaJson.Read(() => new ContextSchemaDefinition(SchemaJson.ParseObject(json, "A context schema")));
    }
}

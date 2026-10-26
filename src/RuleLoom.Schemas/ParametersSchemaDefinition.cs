using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RuleLoom.Schemas;

/// <summary>
/// An action's parameters: the fields of its parameters type, with the required ones listed under <c>required</c>.
/// Built by <see cref="ParametersSchema{T}"/> or read from JSON; written as a JSON Schema 2020-12 document.
/// </summary>
public sealed class ParametersSchemaDefinition : SchemaDefinition
{
    /// <summary>
    /// The options action parameters are described and read with: <see cref="JsonSerializerDefaults.Web"/> (names
    /// ignore case when read and are camelCase when written), enums by name, constructor parameters without a default
    /// and <c>required</c> members required, nullable annotations respected, numbers not read from strings.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    internal ParametersSchemaDefinition(JsonObject document) : base(document) =>
        Fields = SchemaReader.ReadFields(document, document);

    /// <summary>Reads a parameters schema written by <see cref="SchemaDefinition.ToJson"/>.</summary>
    /// <exception cref="JsonException">The JSON isn't a parameters schema.</exception>
    public static ParametersSchemaDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return SchemaJson.Read(() => new ParametersSchemaDefinition(SchemaJson.ParseObject(json, "A parameters schema")));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

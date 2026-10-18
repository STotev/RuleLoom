using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas;

/// <summary>
/// Shared reading and writing of schema documents.
/// </summary>
internal static class SchemaJson
{
    // Indented, readable JSON (no escaping of characters such as '+'); shared by every writer of schema documents.
    public static readonly JsonWriterOptions WriterOptions = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = WriterOptions.Encoder };

    /// <summary>Parses <paramref name="json"/> as a JSON object (a schema document).</summary>
    public static JsonObject ParseObject(string json, string what) =>
        JsonNode.Parse(json) as JsonObject ?? throw new JsonException($"{what} must be a JSON object.");

    /// <summary>
    /// Runs <paramref name="read"/>, reporting the other exceptions JsonNode throws for malformed documents (duplicate
    /// keys, values of the wrong kind) as <see cref="JsonException"/>.
    /// </summary>
    public static T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            throw new JsonException($"The JSON isn't a valid schema document: {ex.Message}", ex);
        }
    }
}

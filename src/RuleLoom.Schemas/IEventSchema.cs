using System.Text.Json;

namespace RuleLoom.Schemas;

/// <summary>
/// An event schema that can be built; implemented by <see cref="EventSchema{T}"/>.
/// </summary>
public interface IEventSchema
{
    /// <summary>
    /// Generates the event's schema.
    /// </summary>
    /// <param name="options">The serializer options the producer sends the event with;
    /// <see cref="JsonSerializerDefaults.Web"/> when omitted.</param>
    EventSchemaDefinition Build(JsonSerializerOptions? options = null);
}

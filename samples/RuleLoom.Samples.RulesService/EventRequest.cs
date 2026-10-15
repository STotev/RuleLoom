using System.Text.Json;

namespace RuleLoom.Samples.RulesService;

/// <summary>
/// The sample's wire format for <c>POST /events</c>. The state stays JSON: the service has no producer classes.
/// </summary>
public sealed class EventRequest
{
    public required string EventType { get; init; }

    public required string TenantId { get; init; }

    /// <summary>
    /// The entity the event is about (an order, an ad); part of the scheduled job keys.
    /// </summary>
    public required string EntityId { get; init; }

    public required JsonElement Current { get; init; }

    public JsonElement? Previous { get; init; }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Abstractions;

/// <summary>
/// An event as rules see it: its type and state, and what the app knows about it besides (its id, subject, source, time
/// and app-defined metadata such as a tenant id). Rules read the envelope with <c>$event.</c> paths
/// (<c>$event.OccurredAt</c>, <c>$event.Metadata.region</c>); actions and operators read it from
/// <see cref="EvaluationContext.Event"/>. RuleLoom defines the envelope and its JSON (<see cref="ToJson"/>,
/// <see cref="Parse"/>), never the transport; deduplicating by <see cref="EventId"/> is the app's or the transport's.
/// </summary>
/// <param name="EventType">The event type rules are written for.</param>
/// <param name="Current">The subject's current state (a .NET object or JSON).</param>
/// <param name="Previous">The previous state, if any: a full copy, or <see cref="PreviousState.FromChanges"/>.</param>
public sealed record RuleEvent(string EventType, object Current, object? Previous = null)
{
    private static readonly JsonSerializerOptions StateJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The producer's id for the event (e.g. a message id), passed through for deduplication and tracing.</summary>
    public string? EventId { get; init; }

    /// <summary>What the event is about, e.g. <c>order-1001</c>.</summary>
    public string? Subject { get; init; }

    /// <summary>Where the event comes from, e.g. a service or a system.</summary>
    public string? Source { get; init; }

    /// <summary>When the event happened.</summary>
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>App-defined values, e.g. a tenant id; read by rules as <c>$event.Metadata.&lt;key&gt;</c> (keys ignore case).</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// Writes the event as JSON: <c>eventType</c>, the envelope's values when set, <c>current</c>, and the previous state
    /// as <c>previous</c> or, when it was built from changes, as <c>changes</c>. State written from .NET objects uses
    /// <see cref="JsonSerializerDefaults.Web"/>.
    /// </summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("eventType", EventType);
            WriteIfSet(writer, "eventId", EventId);
            WriteIfSet(writer, "subject", Subject);
            WriteIfSet(writer, "source", Source);
            if (OccurredAt is { } occurredAt)
                writer.WriteString("occurredAt", occurredAt.ToString("O", CultureInfo.InvariantCulture));

            if (Metadata.Count > 0)
            {
                writer.WritePropertyName("metadata");
                JsonSerializer.Serialize(writer, Metadata, StateJsonOptions);
            }

            writer.WritePropertyName("current");
            WriteState(writer, Current);

            if (Previous is PreviousState fromChanges)
            {
                writer.WritePropertyName("changes");
                WriteState(writer, fromChanges.Changes);
            }
            else if (Previous is not null)
            {
                writer.WritePropertyName("previous");
                WriteState(writer, Previous);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads an event written by <see cref="ToJson"/>. Property names ignore case. The state is read as JSON
    /// (<see cref="JsonElement"/>), which paths read like .NET objects; <c>changes</c> becomes
    /// <see cref="PreviousState.FromChanges"/>. <c>occurredAt</c> must state its offset.
    /// </summary>
    /// <exception cref="JsonException">The JSON isn't an event: malformed, no <c>eventType</c> or <c>current</c>, both
    /// <c>previous</c> and <c>changes</c>, or a value of the wrong type.</exception>
    public static RuleEvent Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("An event must be a JSON object.");

        var properties = root.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);

        var eventType = Text(properties, "eventType") is { Length: > 0 } type ? type
            : throw new JsonException("An event needs a non-empty 'eventType'.");

        if (!properties.TryGetValue("current", out var current) || current.ValueKind == JsonValueKind.Null)
            throw new JsonException("An event needs its 'current' state.");

        var hasPrevious = properties.TryGetValue("previous", out var previous) && previous.ValueKind != JsonValueKind.Null;
        var hasChanges = properties.TryGetValue("changes", out var changes) && changes.ValueKind != JsonValueKind.Null;

        if (hasPrevious && hasChanges)
            throw new JsonException("An event has either 'previous' or 'changes', not both.");

        if (hasChanges && changes.ValueKind != JsonValueKind.Object)
            throw new JsonException("'changes' must be a JSON object.");

        return new RuleEvent(eventType, current, hasChanges ? PreviousState.FromChanges(current, changes) : hasPrevious ? previous : null)
        {
            EventId = Text(properties, "eventId"),
            Subject = Text(properties, "subject"),
            Source = Text(properties, "source"),
            OccurredAt = Text(properties, "occurredAt") is { } occurredAt ? ParseInstant(occurredAt) : null,
            Metadata = properties.TryGetValue("metadata", out var metadata) && metadata.ValueKind != JsonValueKind.Null
                ? metadata.ValueKind == JsonValueKind.Object
                    ? metadata.EnumerateObject().ToDictionary(p => p.Name, p => JsonClrValues.ToClrValue(p.Value))
                    : throw new JsonException("'metadata' must be a JSON object.")
                : new Dictionary<string, object?>()
        };
    }

    private static void WriteIfSet(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value);
    }

    private static void WriteState(Utf8JsonWriter writer, object state) =>
        JsonSerializer.Serialize(writer, state, state.GetType(), StateJsonOptions);

    private static string? Text(Dictionary<string, JsonElement> properties, string name) =>
        !properties.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : throw new JsonException($"'{name}' must be a string.");

    // An event's time is a moment: it must say its offset ("Z" or "+02:00"), so it never depends on a time zone.
    private static DateTimeOffset ParseInstant(string text)
    {
        var hasOffset = text.EndsWith('Z') || text.Length > 6 && text[^6] is '+' or '-' && text[^3] == ':';

        return hasOffset && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            ? instant
            : throw new JsonException($"'occurredAt' must be an ISO 8601 date-time with an offset, e.g. 2026-05-01T09:00:00Z: '{text}'.");
    }
}

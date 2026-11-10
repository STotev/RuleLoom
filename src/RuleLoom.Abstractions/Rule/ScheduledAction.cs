using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RuleLoom.Abstractions.Rule;

/// <summary>
/// An action the engine scheduled instead of running: a reference to it ("rule X's action N, due then"), not a copy, so
/// it stays small to store and runs the rule's current version when it's due. Hand it to your scheduler, keyed by
/// <see cref="Key"/>, and store it with <see cref="ToJson"/>.
/// </summary>
/// <param name="RuleId">The <see cref="RuleDefinition.Id"/> of the rule the action belongs to.</param>
/// <param name="ActionIndex">The action's index in the rule's actions.</param>
/// <param name="ActionName">The action's name, to notice when the rule was edited meanwhile.</param>
/// <param name="ScheduledFor">When the action is due (UTC).</param>
/// <param name="Key">
/// Identifies the pending action, <c>{ruleId}:{actionIndex}:{subject}</c>, so the same event delivered again replaces
/// its job instead of adding one; <c>null</c> when the event has no subject. Use it as the job's id (with your own
/// prefix, e.g. a tenant, when rule ids repeat across tenants).
/// </param>
public sealed record ScheduledAction(string RuleId, int ActionIndex, string ActionName, DateTimeOffset ScheduledFor, string? Key)
{
    /// <summary>
    /// The key of action <paramref name="actionIndex"/> of rule <paramref name="ruleId"/> for <paramref name="subject"/>:
    /// <c>{ruleId}:{actionIndex}:{subject}</c>, with <c>\</c>, <c>:</c>, <c>;</c> and <c>=</c> escaped by a <c>\</c>.
    /// </summary>
    /// <returns><c>null</c> without a subject.</returns>
    public static string? CreateKey(string ruleId, int actionIndex, SubjectIdentity? subject)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        return subject is null ? null : $"{Escape(ruleId)}:{actionIndex.ToString(CultureInfo.InvariantCulture)}:{subject.Text}";
    }

    /// <summary>Writes the scheduled action as JSON (<c>ruleId</c>, <c>actionIndex</c>, <c>actionName</c>,
    /// <c>scheduledFor</c>, <c>key</c> when set), e.g. for a persistent job store.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("ruleId", RuleId);
            writer.WriteNumber("actionIndex", ActionIndex);
            writer.WriteString("actionName", ActionName);
            writer.WriteString("scheduledFor", ScheduledFor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            if (Key is not null)
                writer.WriteString("key", Key);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads a scheduled action written by <see cref="ToJson"/>. Property names ignore case.</summary>
    /// <exception cref="JsonException">The JSON isn't a scheduled action.</exception>
    public static ScheduledAction Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("A scheduled action must be a JSON object.");

        var properties = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);

        var scheduledFor = Text(properties, "scheduledFor");
        if (!DateTimeOffset.TryParseExact(scheduledFor, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due))
            throw new JsonException($"'scheduledFor' must be a round-trip ISO 8601 date-time (as ToJson writes it): '{scheduledFor}'.");

        return new ScheduledAction(
            Text(properties, "ruleId"),
            properties.TryGetValue("actionIndex", out var index) && index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var number) && number >= 0
                ? number
                : throw new JsonException("'actionIndex' must be a non-negative whole number."),
            Text(properties, "actionName"),
            due,
            properties.TryGetValue("key", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null);
    }

    internal static string Escape(string text) =>
        text.Replace(@"\", @"\\").Replace(":", @"\:").Replace(";", @"\;").Replace("=", @"\=");

    private static string Text(Dictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new JsonException($"A scheduled action needs a non-empty '{name}'.");
}

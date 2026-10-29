namespace RuleLoom.Abstractions;

/// <summary>
/// Resolves <c>$event.</c> paths on a <see cref="RuleEvent"/>: its envelope's values by name (ignoring case), metadata
/// by key (exact case wins, else a unique match ignoring case), and anything below them like other paths.
/// </summary>
internal static class EventPaths
{
    /// <summary>The envelope's fields, as <c>$event.</c> paths name them.</summary>
    public static readonly IReadOnlyList<string> Fields = ["EventType", "EventId", "Subject", "Source", "OccurredAt", "Metadata"];

    public static object? Resolve(RuleEvent? @event, RulePath path)
    {
        if (@event is null)
            return null;

        var segments = path.Segments;
        if (segments.Count == 0)
            return @event;

        var value = Field(@event, segments[0]);
        var next = 1;

        if (value is IReadOnlyDictionary<string, object?> metadata && segments.Count > 1)
        {
            value = Find(metadata, segments[1]);
            next = 2;
        }

        return value is null || next == segments.Count
            ? value
            : PathResolver.Shared.Resolve(value, string.Join('.', segments.Skip(next)));
    }

    private static object? Field(RuleEvent @event, string name) => name.ToLowerInvariant() switch
    {
        "eventtype" => @event.EventType,
        "eventid" => @event.EventId,
        "subject" => @event.Subject,
        "source" => @event.Source,
        "occurredat" => @event.OccurredAt,
        "metadata" => @event.Metadata,
        _ => null
    };

    private static object? Find(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var exact))
            return exact;

        var matches = metadata.Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0].Value : null;
    }
}

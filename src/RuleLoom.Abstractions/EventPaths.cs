namespace RuleLoom.Abstractions;

/// <summary>
/// Resolves <c>$event.</c> paths on a <see cref="RuleEvent"/> like other paths on an object, by its properties (ignoring
/// case), so new envelope properties need no change here. Paths don't walk dictionaries, so one step is added: a key of
/// a dictionary property such as <see cref="RuleEvent.Metadata"/> (exact case wins, else a unique match ignoring case).
/// </summary>
internal static class EventPaths
{
    public static object? Resolve(RuleEvent? @event, RulePath path)
    {
        if (@event is null)
            return null;

        var segments = path.Segments;
        if (segments.Count == 0)
            return @event;

        var value = PathResolver.Shared.Resolve(@event, segments[0]);
        var next = 1;

        if (value is IReadOnlyDictionary<string, object?> dictionary && segments.Count > 1)
        {
            value = Find(dictionary, segments[1]);
            next = 2;
        }

        return value is null || next == segments.Count
            ? value
            : PathResolver.Shared.Resolve(value, string.Join('.', segments.Skip(next)));
    }

    private static object? Find(IReadOnlyDictionary<string, object?> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var exact))
            return exact;

        var matches = dictionary.Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0].Value : null;
    }
}

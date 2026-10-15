using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Abstractions;

/// <summary>
/// Resolves the rest of a path inside JSON state (<see cref="JsonElement"/> or <see cref="JsonNode"/>) and returns
/// the value as a plain CLR value: <see cref="string"/>, <see cref="bool"/>, <see cref="long"/>,
/// <see cref="decimal"/> or <see cref="double"/>; objects and arrays are returned as JSON.
/// Keys match like properties: exact case wins, otherwise a unique case-insensitive match; ambiguous keys are missing.
/// </summary>
internal static class JsonPathWalker
{
    public static object? Resolve(object json, string path)
    {
        var segments = path.Split('.');

        return json is JsonElement element
            ? ResolveElement(element, segments)
            : ResolveNode((JsonNode)json, segments);
    }

    private static object? ResolveNode(JsonNode? node, string[] segments)
    {
        foreach (var segment in segments)
        {
            // Only objects have keys; a JsonValue never wraps an object or array.
            if (node is not JsonObject obj || !TryFind(obj, segment, out node))
                return null;
        }

        return ToClrValue(node);
    }

    private static object? ResolveElement(JsonElement element, string[] segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !TryFind(element.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value)), segments[i], out element))
            {
                return null;
            }
        }

        return ToClrValue(element);
    }

    private static bool TryFind<T>(IEnumerable<KeyValuePair<string, T>> properties, string name, out T value)
    {
        value = default!;
        T? match = default;
        var exactCount = 0;
        var ignoreCaseCount = 0;

        foreach (var (key, candidate) in properties)
        {
            if (key == name)
            {
                value = candidate;
                exactCount++;
            }
            else if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                match = candidate;
                ignoreCaseCount++;
            }
        }

        if (exactCount > 0)
            return exactCount == 1;

        value = match!;
        return ignoreCaseCount == 1;
    }

    private static object? ToClrValue(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<JsonElement>(out var element) => ToClrValue(element),
        JsonValue value => value.GetValue<object>(),
        _ => node
    };

    private static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => ToNumber(element),
        JsonValueKind.Object or JsonValueKind.Array => element,
        _ => null
    };

    private static object? ToNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var integer))
            return integer;
        if (element.TryGetDecimal(out var number))
            return number;
        if (element.TryGetDouble(out var real) && double.IsFinite(real))
            return real;

        return null;
    }
}

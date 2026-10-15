using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Abstractions;

/// <summary>
/// Resolves property paths on objects using compiled expression trees for performance.
/// Returns null for missing properties instead of throwing exceptions.
/// Path segments match property names ignoring case; an exact-case match takes precedence.
/// Paths continue through the runtime value where the declared type ends: into JSON state
/// (<see cref="JsonElement"/>, <see cref="JsonNode"/>) and into properties declared as <see cref="object"/>,
/// an interface or a base type. JSON values are returned as plain CLR values.
/// Accessors are cached per (type, path), including paths that don't exist. Instances are thread-safe.
/// </summary>
public class PathResolver
{
    /// <summary>
    /// The resolver shared by all <see cref="EvaluationContext"/> instances, so accessors are compiled once per process.
    /// </summary>
    internal static PathResolver Shared { get; } = new();

    private readonly ConcurrentDictionary<(Type Type, string Path), Accessor> _cache = new();

    /// <summary>
    /// Resolves a property path on the given object.
    /// Returns null if the object is null or if any property in the path doesn't exist.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The path reaches a <see cref="JsonElement"/> whose
    /// <see cref="JsonDocument"/> has been disposed.</exception>
    public object? Resolve(object? root, string path)
    {
        if (root == null) return null;
        if (string.IsNullOrWhiteSpace(path)) return root;

        var current = root;

        while (true)
        {
            if (current is JsonElement or JsonNode)
                return JsonPathWalker.Resolve(current, path);

            var accessor = _cache.GetOrAdd((current.GetType(), path), static key => BuildAccessor(key.Path, key.Type));
            var value = accessor.Get(current);

            // The whole path resolved on declared types, or the value it stopped at is missing.
            if (accessor.RemainingPath is null || value is null)
                return value;

            // Same type as declared: the next property doesn't exist. Otherwise continue on the runtime value.
            if (value is not (JsonElement or JsonNode) && value.GetType() == accessor.StopType)
                return null;

            current = value;
            path = accessor.RemainingPath;
        }
    }

    internal bool IsCached(Type type, string path) => _cache.ContainsKey((type, path));

    /// <summary>
    /// Compiles the longest prefix of <paramref name="path"/> that resolves on declared property types.
    /// </summary>
    private static Accessor BuildAccessor(string path, Type rootType)
    {
        var segments = path.Split('.');
        var param = Expression.Parameter(typeof(object), "obj");
        Expression current = Expression.Convert(param, rootType);
        var consumed = 0;

        while (consumed < segments.Length && FindProperty(current.Type, segments[consumed]) is { } prop)
        {
            current = Expression.Property(current, prop);
            consumed++;
        }

        var remainingPath = consumed == segments.Length ? null : string.Join('.', segments[consumed..]);
        if (consumed == 0)
            return new Accessor(static obj => obj, remainingPath, rootType);

        var getter = Expression.Lambda<Func<object, object?>>(Expression.Convert(current, typeof(object)), param).Compile();

        return new Accessor(obj =>
        {
            try
            {
                return getter(obj);
            }
            catch
            {
                // Return null if resolution fails (e.g., null reference in path)
                return null;
            }
        }, remainingPath, current.Type);
    }

    /// <summary>
    /// Finds a property by exact name, or else by a unique case-insensitive match (ambiguous matches count as missing).
    /// </summary>
    private static PropertyInfo? FindProperty(Type type, string name)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();

        var exact = properties.Where(p => p.Name == name).Take(2).ToArray();
        if (exact.Length > 0)
            return exact.Length == 1 ? exact[0] : null;

        var matches = properties
            .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>
    /// A compiled path prefix: <see cref="Get"/> resolves it, <see cref="RemainingPath"/> is what's left
    /// (null when the whole path resolved), and <see cref="StopType"/> is the declared type where it stopped.
    /// </summary>
    private sealed record Accessor(Func<object, object?> Get, string? RemainingPath, Type StopType);
}

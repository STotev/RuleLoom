using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace RuleForge.Abstractions;

/// <summary>
/// Resolves property paths on objects using compiled expression trees for performance.
/// Returns null for missing properties instead of throwing exceptions.
/// Accessors are cached per (type, path), including paths that don't exist. Instances are thread-safe.
/// </summary>
public class PathResolver
{
    /// <summary>
    /// The resolver shared by all <see cref="EvaluationContext"/> instances, so accessors are compiled once per process.
    /// </summary>
    internal static PathResolver Shared { get; } = new();

    private readonly ConcurrentDictionary<(Type Type, string Path), Func<object, object?>?> _cache = new();

    /// <summary>
    /// Resolves a property path on the given object.
    /// Returns null if the object is null or if any property in the path doesn't exist.
    /// </summary>
    public object? Resolve(object? root, string path)
    {
        if (root == null) return null;
        if (string.IsNullOrWhiteSpace(path)) return root;

        var accessor = _cache.GetOrAdd((root.GetType(), path), static key => BuildAccessor(key.Path, key.Type));
        if (accessor == null) return null;

        try
        {
            return accessor(root);
        }
        catch
        {
            // Return null if resolution fails (e.g., null reference in path)
            return null;
        }
    }

    internal bool IsCached(Type type, string path) => _cache.ContainsKey((type, path));

    private static Func<object, object?>? BuildAccessor(string path, Type rootType)
    {
        try
        {
            var param = Expression.Parameter(typeof(object), "obj");
            Expression current = Expression.Convert(param, rootType);

            foreach (var part in path.Split('.'))
            {
                var prop = current.Type.GetProperty(part);
                if (prop == null)
                {
                    // Property doesn't exist, return null accessor
                    return null;
                }

                current = Expression.Property(current, prop);
            }

            var convert = Expression.Convert(current, typeof(object));
            return Expression.Lambda<Func<object, object?>>(convert, param).Compile();
        }
        catch
        {
            // Failed to build accessor, return null accessor
            return null;
        }
    }
}

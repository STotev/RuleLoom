using System.Linq.Expressions;

namespace BusinessRules.Abstractions;

/// <summary>
/// Resolves property paths on objects using compiled expression trees for performance.
/// Returns null for missing properties instead of throwing exceptions.
/// </summary>
public class PathResolver
{
    private readonly Dictionary<(Type, string), Func<object, object?>> _cache = new();

    /// <summary>
    /// Resolves a property path on the given object.
    /// Returns null if the object is null or if any property in the path doesn't exist.
    /// </summary>
    public object? Resolve(object? root, string path)
    {
        if (root == null) return null;
        if (string.IsNullOrWhiteSpace(path)) return root;

        if (!_cache.TryGetValue((root.GetType(), path), out var accessor))
        {
            accessor = BuildAccessor(path, root.GetType());
            if (accessor == null) return null;

            _cache[(root.GetType(), path)] = accessor;
        }

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

    private Func<object, object?>? BuildAccessor(string path, Type rootType)
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

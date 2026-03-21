using System.Linq.Expressions;

namespace BusinessRules.Abstractions;

public class PathResolver
{
    private readonly Dictionary<(Type, string), Func<object, object?>> _cache = new();

    public object? Resolve(object root, string path)
    {
        if (root == null) return null;

        if (!_cache.TryGetValue((root.GetType(), path), out var accessor))
        {
            accessor = BuildAccessor(path, root.GetType());
            _cache[(root.GetType(), path)] = accessor;
        }

        return accessor(root);
    }

    private Func<object, object?> BuildAccessor(string path, Type rootType)
    {
        var param = Expression.Parameter(typeof(object), "obj");
        Expression current = Expression.Convert(param, rootType); // <-- use actual type

        foreach (var part in path.Split('.'))
        {
            var prop = current.Type.GetProperty(part)
                       ?? throw new InvalidOperationException($"Property {part} not found on type {current.Type.Name}");

            current = Expression.Property(current, prop);
        }

        var convert = Expression.Convert(current, typeof(object));
        return Expression.Lambda<Func<object, object?>>(convert, param).Compile();
    }
}

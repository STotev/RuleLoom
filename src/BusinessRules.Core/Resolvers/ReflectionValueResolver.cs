using System.Reflection;
using BusinessRules.Abstractions;

namespace BusinessRules.Core.Resolvers;

public class ReflectionValueResolver : IValueResolver
{
    public object? Resolve(string path, object obj)
    {
        var parts = path.Split('.');
        var current = obj;

        foreach (var part in parts)
        {
            if (current == null) return null;

            var prop = current.GetType().GetProperty(part,
                BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            if (prop == null) return null;

            current = prop.GetValue(current);
        }

        return current;
    }
}
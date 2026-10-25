namespace RuleLoom;

/// <summary>Creates actions and providers that the direct factories have no instance for.</summary>
internal static class Activation
{
    public static T Create<T>(Type type, string name) where T : class
    {
        if (!typeof(T).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Can't create {type.Name} for '{name}': add an instance to the factory, or give it a public parameterless constructor.");
        }

        return (T)Activator.CreateInstance(type)!;
    }
}

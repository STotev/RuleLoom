namespace BusinessRules.Abstractions;

public class EvaluationContext
{
    private readonly PathResolver _resolver = new();

    public object? Current { get; }
    public object? Previous { get; }

    public EvaluationContext(object? current, object? previous = null)
    {
        Current = current;
        Previous = previous;
    }

    public object? Resolve(string path)
    {
        var parts = path.Split('.', 2);

        object? root;
        string remainingPath;

        if (parts.Length == 1)
        {
            root = Current;
            remainingPath = parts[0];
        }
        else
        {
            var scope = parts[0].ToLower();

            root = scope switch
            {
                "current" => Current,
                "previous" => Previous,
                _ => throw new InvalidOperationException($"Unknown root '{scope}'")
            };

            remainingPath = parts[1];
        }

        if (root == null)
            return null;

        return _resolver.Resolve(root, remainingPath); // <-- passes correct root object
    }
}

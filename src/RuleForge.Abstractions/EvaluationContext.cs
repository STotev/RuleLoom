namespace RuleForge.Abstractions;

/// <summary>
/// Provides the evaluation context for rule execution, containing current and previous state.
/// </summary>
public class EvaluationContext
{
    private readonly PathResolver _resolver = new();

    /// <summary>
    /// The current state of the object being evaluated.
    /// </summary>
    public object? Current { get; }
    
    /// <summary>
    /// The previous state of the object (optional). Used for change detection.
    /// </summary>
    public object? Previous { get; }

    /// <summary>
    /// Creates a new evaluation context.
    /// </summary>
    /// <param name="current">The current state.</param>
    /// <param name="previous">The previous state (optional).</param>
    public EvaluationContext(object? current, object? previous = null)
    {
        Current = current;
        Previous = previous;
    }

    /// <summary>
    /// Resolves a property path from the context.
    /// Supports "current.Property", "previous.Property", or just "Property" (defaults to current).
    /// </summary>
    /// <param name="path">The property path to resolve.</param>
    /// <returns>The resolved value, or null if the path doesn't exist.</returns>
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

        return _resolver.Resolve(root, remainingPath);
    }
}

namespace RuleLoom.Abstractions;

/// <summary>
/// Provides the evaluation context for rule execution, containing current and previous state.
/// </summary>
public class EvaluationContext
{
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
    /// A leading <c>$current.</c> or <c>$previous.</c> (case-insensitive) selects the state; any other path,
    /// including nested ones such as <c>Address.City</c>, resolves against <see cref="Current"/>.
    /// <c>$current</c> or <c>$previous</c> alone returns the whole state object. Other names starting with <c>$</c>
    /// are reserved.
    /// </summary>
    /// <param name="path">The property path to resolve.</param>
    /// <returns>The resolved value, or null if the path doesn't exist.</returns>
    /// <exception cref="InvalidOperationException">The path starts with an unknown <c>$</c> variable.</exception>
    public object? Resolve(string path)
    {
        var parts = path.Split('.', 2);

        object? root = Current;
        var remainingPath = path;

        if (parts[0].StartsWith('$'))
        {
            if (parts[0].Equals("$previous", StringComparison.OrdinalIgnoreCase))
            {
                root = Previous;
            }
            else if (!parts[0].Equals("$current", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unknown variable '{parts[0]}' in path '{path}'. Use $current or $previous.");
            }

            remainingPath = parts.Length == 2 ? parts[1] : string.Empty;
        }

        if (root == null)
            return null;

        return PathResolver.Shared.Resolve(root, remainingPath);
    }
}

namespace RuleForge.Abstractions.Action;

/// <summary>
/// Resolves action executors by the name they are registered under.
/// </summary>
public interface IActionExecutorFactory
{
    /// <summary>
    /// Tries to get the action executor registered under <paramref name="name"/> (matched ignoring case).
    /// </summary>
    /// <param name="name">The action name (e.g. "log").</param>
    /// <param name="executor">The resolved executor when found.</param>
    /// <returns><c>true</c> when an executor is found; otherwise <c>false</c>.</returns>
    bool TryGetExecutor(string name, out IActionExecutor? executor);

    /// <summary>
    /// The action names this factory can resolve. Used to validate rules before they run.
    /// </summary>
    IReadOnlyCollection<string> ActionNames { get; }
}

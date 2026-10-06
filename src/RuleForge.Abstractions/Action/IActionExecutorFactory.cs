namespace RuleForge.Abstractions.Action;

/// <summary>
/// Resolves action executors by action type.
/// </summary>
public interface IActionExecutorFactory
{
    /// <summary>
    /// Tries to get an action executor for the provided action type.
    /// </summary>
    /// <param name="type">The action type (e.g. "log").</param>
    /// <param name="executor">The resolved executor when found.</param>
    /// <returns><c>true</c> when an executor is found; otherwise <c>false</c>.</returns>
    bool TryGetExecutor(string type, out IActionExecutor? executor);

    /// <summary>
    /// The action types this factory can resolve. Used to validate rules before they run.
    /// </summary>
    IReadOnlyCollection<string> ActionTypes { get; }
}

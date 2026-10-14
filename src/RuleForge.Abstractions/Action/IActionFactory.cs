namespace RuleForge.Abstractions.Action;

/// <summary>
/// Resolves actions by the name they are registered under.
/// </summary>
public interface IActionFactory
{
    /// <summary>
    /// Tries to get the action registered under <paramref name="name"/> (matched ignoring case).
    /// </summary>
    /// <param name="name">The action name (e.g. "send-email").</param>
    /// <param name="action">The resolved action when found.</param>
    /// <returns><c>true</c> when an action is found; otherwise <c>false</c>.</returns>
    bool TryGetAction(string name, out IAction? action);

    /// <summary>
    /// The action names this factory can resolve. Used to validate rules before they run.
    /// </summary>
    IReadOnlyCollection<string> ActionNames { get; }
}

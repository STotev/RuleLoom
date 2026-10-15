using RuleLoom.Abstractions.Action;

namespace RuleLoom.Actions;

/// <summary>
/// Default non-DI action factory backed by action instances, each registered under a name: the one given
/// to <see cref="Add"/>, or else its default name (<see cref="ActionNames.GetDefaultName"/>). Names are matched
/// ignoring case.
/// </summary>
public sealed class DirectActionFactory : IActionFactory
{
    private readonly Dictionary<string, IAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an empty factory; register actions with <see cref="Add"/>.
    /// </summary>
    public DirectActionFactory()
    {
    }

    /// <summary>
    /// Creates a factory with each action registered under its default name.
    /// </summary>
    public DirectActionFactory(IEnumerable<IAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        foreach (var action in actions)
            Add(action);
    }

    public IReadOnlyCollection<string> ActionNames => _actions.Keys;

    /// <summary>
    /// Registers <paramref name="action"/> under <paramref name="name"/>, or under its default name when omitted.
    /// </summary>
    /// <exception cref="ArgumentException">The name is already registered (names are case-insensitive).</exception>
    public DirectActionFactory Add(IAction action, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        name ??= Abstractions.Action.ActionNames.GetDefaultName(action.GetType());

        if (_actions.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            throw new ArgumentException(
                existing == name
                    ? $"Action name '{name}' is registered more than once."
                    : $"Action name '{name}' conflicts with '{existing}' (names are case-insensitive).",
                nameof(name));
        }

        _actions.Add(name, action);
        return this;
    }

    public bool TryGetAction(string name, out IAction? action) =>
        _actions.TryGetValue(name, out action);
}

using RuleLoom.Abstractions.Action;

namespace RuleLoom.Actions;

/// <summary>
/// Default non-DI action factory: returns the instance added for an action's type, or else creates the type with its
/// parameterless constructor. Names stay in the catalog; the factory only knows types.
/// </summary>
public sealed class DirectActionFactory : IActionFactory
{
    private readonly Dictionary<Type, IAction> _actions = [];

    /// <summary>
    /// Creates a factory without instances: every action is created with its parameterless constructor.
    /// </summary>
    public DirectActionFactory()
    {
    }

    /// <summary>
    /// Creates a factory that returns these instances for their types.
    /// </summary>
    public DirectActionFactory(IEnumerable<IAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        foreach (var action in actions)
            Add(action);
    }

    /// <summary>
    /// Returns <paramref name="action"/> for every registration of its type.
    /// </summary>
    /// <exception cref="ArgumentException">An instance of this type was already added.</exception>
    public DirectActionFactory Add(IAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_actions.TryAdd(action.GetType(), action))
            throw new ArgumentException($"An instance of {action.GetType().Name} was already added.", nameof(action));

        return this;
    }

    /// <inheritdoc />
    public IAction Create(ActionRegistration action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return _actions.TryGetValue(action.Type, out var instance) ? instance : Activation.Create<IAction>(action.Type, action.Name);
    }
}

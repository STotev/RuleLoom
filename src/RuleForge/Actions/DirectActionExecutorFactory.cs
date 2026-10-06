using RuleForge.Abstractions.Action;

namespace RuleForge.Actions;

/// <summary>
/// Default non-DI action executor factory backed by executor instances, each registered under a name: the one given
/// to <see cref="Add"/>, or else its default name (<see cref="ActionNames.GetDefaultName"/>). Names are matched
/// ignoring case.
/// </summary>
public sealed class DirectActionExecutorFactory : IActionExecutorFactory
{
    private readonly Dictionary<string, IActionExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an empty factory; register executors with <see cref="Add"/>.
    /// </summary>
    public DirectActionExecutorFactory()
    {
    }

    /// <summary>
    /// Creates a factory with each executor registered under its default name.
    /// </summary>
    public DirectActionExecutorFactory(IEnumerable<IActionExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);

        foreach (var executor in executors)
            Add(executor);
    }

    public IReadOnlyCollection<string> ActionNames => _executors.Keys;

    /// <summary>
    /// Registers <paramref name="executor"/> under <paramref name="name"/>, or under its default name when omitted.
    /// </summary>
    /// <exception cref="ArgumentException">The name is already registered (names are case-insensitive).</exception>
    public DirectActionExecutorFactory Add(IActionExecutor executor, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(executor);

        name ??= Abstractions.Action.ActionNames.GetDefaultName(executor.GetType());

        if (_executors.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            throw new ArgumentException(
                existing == name
                    ? $"Action name '{name}' is registered more than once."
                    : $"Action name '{name}' conflicts with '{existing}' (names are case-insensitive).",
                nameof(name));
        }

        _executors.Add(name, executor);
        return this;
    }

    public bool TryGetExecutor(string name, out IActionExecutor? executor) =>
        _executors.TryGetValue(name, out executor);
}

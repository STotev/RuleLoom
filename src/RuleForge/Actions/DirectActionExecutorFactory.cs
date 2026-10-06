using RuleForge.Abstractions.Action;

namespace RuleForge.Actions;

/// <summary>
/// Default non-DI action executor factory backed by direct executor instances.
/// Action types are matched ignoring case.
/// </summary>
public sealed class DirectActionExecutorFactory : IActionExecutorFactory
{
    private readonly Dictionary<string, IActionExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);

    public DirectActionExecutorFactory(IEnumerable<IActionExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);

        foreach (var executor in executors)
        {
            if (_executors.TryGetValue(executor.Type, out var existing))
            {
                throw new ArgumentException(
                    existing.Type == executor.Type
                        ? $"Action type '{executor.Type}' is registered more than once."
                        : $"Action type '{executor.Type}' conflicts with '{existing.Type}' (types are case-insensitive).",
                    nameof(executors));
            }

            _executors.Add(executor.Type, executor);
        }
    }

    public IReadOnlyCollection<string> ActionTypes => _executors.Keys;

    public bool TryGetExecutor(string type, out IActionExecutor? executor) =>
        _executors.TryGetValue(type, out executor);
}

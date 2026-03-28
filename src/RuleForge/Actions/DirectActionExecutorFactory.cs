using RuleForge.Abstractions.Action;

namespace RuleForge.Actions;

/// <summary>
/// Default non-DI action executor factory backed by direct executor instances.
/// </summary>
public sealed class DirectActionExecutorFactory : IActionExecutorFactory
{
    private readonly Dictionary<string, IActionExecutor> _executors;

    public DirectActionExecutorFactory(IEnumerable<IActionExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors.ToDictionary(x => x.Type);
    }

    public bool TryGetExecutor(string type, out IActionExecutor? executor) =>
        _executors.TryGetValue(type, out executor);
}

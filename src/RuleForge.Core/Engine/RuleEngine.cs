using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;

namespace RuleForge.Core.Engine;

public sealed class RuleEngine
{
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly Dictionary<string, IActionExecutor> _actions;

    public RuleEngine(
        IConditionEvaluator conditionEvaluator,
        IEnumerable<IActionExecutor> actions)
    {
        _conditionEvaluator = conditionEvaluator ?? throw new ArgumentNullException(nameof(conditionEvaluator));
        
        ArgumentNullException.ThrowIfNull(actions);
        _actions = actions.ToDictionary(x => x.Type);
    }

    public async Task ExecuteAsync(string eventType, object current, object? previous,
        IEnumerable<RuleDefinition> rules, CancellationToken cancellationToken = default)
    {
        var context = new EvaluationContext(current, previous);

        var matchingRules = rules
            .Where(r => r.EventType == eventType)
            .OrderByDescending(r => r.Priority)
            .ToList();

        foreach (var rule in matchingRules)
        {
            try
            {
                if (!_conditionEvaluator.Evaluate(context, rule.Condition))
                    continue;

                foreach (var action in rule.Actions)
                {
                    if (action.Condition is not null && !_conditionEvaluator.Evaluate(context, action.Condition))
                        continue;

                    if (_actions.TryGetValue(action.Type, out var executor))
                    {
                        await executor.ExecuteAsync(action, context, cancellationToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log error but continue with next rule to prevent one failing rule from stopping all execution
                // TODO: Replace with structured logging when observability is implemented (Phase 4.3)
                Console.Error.WriteLine($"[RuleForge] Rule '{rule.Name}' (ID: {rule.Id}) failed: {ex.GetType().Name}: {ex.Message}");
                continue;
            }
        }
    }

    public Task ExecuteAsync(string eventType, object data, IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(eventType, data, null, rules, cancellationToken);
    }
}

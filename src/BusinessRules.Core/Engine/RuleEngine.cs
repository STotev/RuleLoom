using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Action;
using BusinessRules.Abstractions.Condition;
using BusinessRules.Abstractions.Rule;
using BusinessRules.Core.Helpers;

namespace BusinessRules.Core.Engine;

public class RuleEngine
{
    private readonly IExpressionEvaluator _conditionEvaluator;
    private readonly Dictionary<string, IActionExecutor> _actions;

    public RuleEngine(
        IExpressionEvaluator conditionEvaluator,
        IEnumerable<IActionExecutor> actions)
    {
        _conditionEvaluator = conditionEvaluator;
        _actions = actions.ToDictionary(x => x.Type);
    }

    public async Task ExecuteAsync(string eventType, object current, object? previous, IEnumerable<RuleDefinition> rules)
    {
        var context = new EvaluationContext(current, previous);

        var matchingRules = rules.Where(r => r.EventType == eventType);

        foreach (var rule in matchingRules)
        {
            if (!BoolHelper.ToBool(_conditionEvaluator.Evaluate(context, rule.Condition)))
                continue;

            foreach (var action in rule.Actions)
            {
                if (action.Condition is not null && !BoolHelper.ToBool(_conditionEvaluator.Evaluate(context, action.Condition)))
                    continue;

                if (_actions.TryGetValue(action.Type, out var executor))
                {
                    await executor.ExecuteAsync(action, context);
                }
            }
        }
    }

    public Task ExecuteAsync(string eventType, object data, IEnumerable<RuleDefinition> rules)
    {
        return ExecuteAsync(eventType, data, null, rules);
    }
}

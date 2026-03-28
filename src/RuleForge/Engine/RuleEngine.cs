using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;

namespace RuleForge.Engine;

public sealed class RuleEngine
{
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly IActionExecutorFactory _actionFactory;

    public RuleEngine(
        IConditionEvaluator conditionEvaluator,
        IActionExecutorFactory actionFactory)
    {
        _conditionEvaluator = conditionEvaluator ?? throw new ArgumentNullException(nameof(conditionEvaluator));
        _actionFactory = actionFactory ?? throw new ArgumentNullException(nameof(actionFactory));
    }

    public async Task<IReadOnlyList<RuleExecutionResult>> ExecuteAsync(string eventType, object current, object? previous,
        IEnumerable<RuleDefinition> rules, CancellationToken cancellationToken = default)
    {
        var context = new EvaluationContext(current, previous);
        var ruleResults = new List<RuleExecutionResult>();

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

                var actionResults = new List<ActionResult>();

                foreach (var action in rule.Actions)
                {
                    if (action.Condition is not null && !_conditionEvaluator.Evaluate(context, action.Condition))
                    {
                        actionResults.Add(ActionResult.Skipped($"Action '{action.Type}' skipped because condition was not met."));
                        continue;
                    }

                    if (!_actionFactory.TryGetExecutor(action.Type, out var executor) || executor is null)
                    {
                        actionResults.Add(
                            ActionResult.Failed(
                                new InvalidOperationException($"No action executor is registered for action type '{action.Type}'."),
                                $"Action '{action.Type}' is not supported."));
                        continue;
                    }

                    try
                    {
                        var actionResult = await executor.ExecuteAsync(action, context, cancellationToken);
                        actionResults.Add(actionResult);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        actionResults.Add(ActionResult.Failed(ex, $"Action '{action.Type}' execution failed."));
                    }
                }

                ruleResults.Add(new RuleExecutionResult
                {
                    RuleName = rule.Name,
                    ActionResults = actionResults
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log error but continue with next rule to prevent one failing rule from stopping all execution
                // TODO: Replace with structured logging when observability is implemented (Phase 4.3)
                Console.Error.WriteLine($"[RuleForge] Rule '{rule.Name}' (ID: {rule.Id}) failed: {ex.GetType().Name}: {ex.Message}");
                continue;
            }
        }

        return ruleResults;
    }

    public Task<IReadOnlyList<RuleExecutionResult>> ExecuteAsync(string eventType, object data, IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(eventType, data, null, rules, cancellationToken);
    }
}

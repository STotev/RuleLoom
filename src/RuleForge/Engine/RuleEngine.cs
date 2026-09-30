using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Abstractions.Scheduling;
using RuleForge.Scheduling;

namespace RuleForge.Engine;

public sealed class RuleEngine(
    IConditionEvaluator conditionEvaluator,
    IActionExecutorFactory actionFactory,
    TimeProvider? timeProvider = null)
{
    private readonly IConditionEvaluator _conditionEvaluator =
        conditionEvaluator ?? throw new ArgumentNullException(nameof(conditionEvaluator));

    private readonly IActionExecutorFactory _actionFactory =
        actionFactory ?? throw new ArgumentNullException(nameof(actionFactory));

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<RuleExecutionResult>> ExecuteAsync(
        string eventType,
        object current,
        object? previous,
        IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default)
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
                {
                    continue;
                }

                var actionResults = new List<ActionResult>();

                foreach (var action in rule.Actions)
                {
                    if (action.Condition is not null && !_conditionEvaluator.Evaluate(context, action.Condition))
                    {
                        actionResults.Add(
                            ActionResult.Skipped($"Action '{action.Type}' skipped because condition was not met."));

                        continue;
                    }

                    if (action.Schedule is { Mode: not ScheduleMode.Immediate } schedule)
                    {
                        try
                        {
                            var scheduledFor =
                                ScheduleCalculator.Calculate(schedule, context, _timeProvider.GetUtcNow());

                            actionResults.Add(
                                ActionResult.Scheduled(
                                    scheduledFor,
                                    $"Action '{action.Type}' scheduled for {scheduledFor:O}."));
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            actionResults.Add(ActionResult.Failed(ex, $"Action '{action.Type}' scheduling failed. Reason: {ex.Message}"));
                        }

                        continue;
                    }

                    if (!_actionFactory.TryGetExecutor(action.Type, out var executor) || executor is null)
                    {
                        var ex = new InvalidOperationException(
                            $"No action executor is registered for action type '{action.Type}'.");

                        actionResults.Add(ActionResult.Failed(ex, $"Action '{action.Type}' is not supported."));

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
                await Console.Error.WriteLineAsync(
                    $"[RuleForge] Rule '{rule.Name}' (ID: {rule.Id}) failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return ruleResults;
    }

    public Task<IReadOnlyList<RuleExecutionResult>> ExecuteAsync(
        string eventType,
        object data,
        IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(eventType, data, null, rules, cancellationToken);
    }
}

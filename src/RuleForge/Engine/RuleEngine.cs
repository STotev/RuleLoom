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

    /// <summary>
    /// Evaluates every rule for <paramref name="eventType"/> against the current and previous state and processes
    /// the actions of the rules that match.
    /// </summary>
    /// <returns>One result per rule whose event type matched; failures are reported in the results, not thrown.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<RuleEngineResult> ExecuteAsync(
        string eventType,
        object current,
        object? previous,
        IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default)
    {
        var context = new EvaluationContext(current, previous);
        var ruleResults = new List<RuleExecutionResult>();
        var scheduledActions = new List<ScheduledAction>();

        var matchingRules = rules
            .Where(r => r.EventType == eventType)
            .OrderByDescending(r => r.Priority);

        foreach (var rule in matchingRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool matched;
            try
            {
                matched = _conditionEvaluator.Evaluate(context, rule.Condition);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ruleResults.Add(CreateRuleResult(rule, RuleExecutionStatus.Failed, error: ex));
                continue;
            }

            if (!matched)
            {
                ruleResults.Add(CreateRuleResult(rule, RuleExecutionStatus.NotMatched));
                continue;
            }

            var actionResults = new List<ActionResult>(rule.Actions.Count);

            for (var index = 0; index < rule.Actions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var action = rule.Actions[index];
                var result = await ProcessActionAsync(action, context, cancellationToken)
                    with { ActionType = action.Type, ActionIndex = index };

                actionResults.Add(result);

                if (result.Status == ActionExecutionStatus.Scheduled)
                    scheduledActions.Add(new ScheduledAction(rule.Id, rule.Name, action, result));
            }

            ruleResults.Add(CreateRuleResult(rule, RuleExecutionStatus.Matched, actionResults));
        }

        return new RuleEngineResult { Rules = ruleResults, ScheduledActions = scheduledActions };
    }

    /// <summary>
    /// Evaluates rules for an event that has no previous state.
    /// </summary>
    public Task<RuleEngineResult> ExecuteAsync(
        string eventType,
        object data,
        IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(eventType, data, null, rules, cancellationToken);
    }

    private async Task<ActionResult> ProcessActionAsync(
        ActionDefinition action,
        EvaluationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            if (action.Condition is not null && !_conditionEvaluator.Evaluate(context, action.Condition))
                return ActionResult.Skipped($"Action '{action.Type}' skipped because condition was not met.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex, $"Action '{action.Type}' condition could not be evaluated. Reason: {ex.Message}");
        }

        if (action.Schedule is { Mode: not ScheduleMode.Immediate } schedule)
        {
            try
            {
                var scheduledFor = ScheduleCalculator.Calculate(schedule, context, _timeProvider.GetUtcNow());

                return ActionResult.Scheduled(scheduledFor, $"Action '{action.Type}' scheduled for {scheduledFor:O}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ActionResult.Failed(ex, $"Action '{action.Type}' scheduling failed. Reason: {ex.Message}");
            }
        }

        if (!_actionFactory.TryGetExecutor(action.Type, out var executor) || executor is null)
        {
            var ex = new InvalidOperationException($"No action executor is registered for action type '{action.Type}'.");

            return ActionResult.Failed(ex, $"Action '{action.Type}' is not supported.");
        }

        try
        {
            // Guards against executors that return null despite the non-nullable contract.
            ActionResult? result = await executor.ExecuteAsync(action, context, cancellationToken);

            return result ?? ActionResult.Failed(
                new InvalidOperationException($"The executor for action type '{action.Type}' returned no result."),
                $"Action '{action.Type}' execution failed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex, $"Action '{action.Type}' execution failed.");
        }
    }

    private static RuleExecutionResult CreateRuleResult(
        RuleDefinition rule,
        RuleExecutionStatus status,
        IReadOnlyList<ActionResult>? actionResults = null,
        Exception? error = null) =>
        new()
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            Status = status,
            Error = error,
            ActionResults = actionResults ?? []
        };
}

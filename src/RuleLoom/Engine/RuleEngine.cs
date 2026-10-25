using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Actions;
using RuleLoom.Contexts;
using RuleLoom.Evaluators;
using RuleLoom.Scheduling;

namespace RuleLoom.Engine;

/// <summary>
/// Runs rules on a <see cref="Catalog"/>: conditions use its operators, actions and contexts are found in it by name and
/// created by the factories.
/// </summary>
/// <param name="catalog">The catalog the rules are written against.</param>
/// <param name="actions">Creates actions; by default a <see cref="DirectActionFactory"/> without instances.</param>
/// <param name="contextProviders">Creates context providers; by default a <see cref="DirectContextProviderFactory"/>
/// without instances.</param>
/// <param name="timeProvider">The clock schedules are computed with; the system clock by default.</param>
public sealed class RuleEngine(
    Catalog catalog,
    IActionFactory? actions = null,
    IContextProviderFactory? contextProviders = null,
    TimeProvider? timeProvider = null)
{
    private readonly Catalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly ConditionEvaluator _conditionEvaluator = new(catalog);
    private readonly IActionFactory _actionFactory = actions ?? new DirectActionFactory();
    private readonly IContextProviderFactory _contextProviders = contextProviders ?? new DirectContextProviderFactory();
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
        var ruleResults = new List<RuleExecutionResult>();
        var scheduledActions = new List<ScheduledAction>();

        var matchingRules = rules
            .Where(r => string.Equals(r.EventType, eventType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Priority)
            .ToList();

        var context = new EvaluationContext(current, previous, eventType, _catalog, _contextProviders);

        await PrefetchContextsAsync(eventType, context, matchingRules, cancellationToken);

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
                    with { ActionName = action.Action, ActionIndex = index };

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

    /// <summary>
    /// Loads the contexts the rules use, in parallel. Failures stay cached in the context, so only the rules that
    /// resolve a failed context fail; contexts the event doesn't declare fail when a rule resolves them.
    /// </summary>
    private async Task PrefetchContextsAsync(
        string eventType,
        EvaluationContext context,
        IReadOnlyList<RuleDefinition> rules,
        CancellationToken cancellationToken)
    {
        var names = ContextReferences.Collect(rules).Where(name => _catalog.TryGetContext(eventType, name, out _, out _));

        await Task.WhenAll(names.Select(async name =>
        {
            try
            {
                await context.GetContextAsync(name, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Reported by the rules that use the context.
            }
        }));
    }

    private async Task<ActionResult> ProcessActionAsync(
        ActionDefinition action,
        EvaluationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            if (action.Condition is not null && !_conditionEvaluator.Evaluate(context, action.Condition))
                return ActionResult.Skipped($"Action '{action.Action}' skipped because condition was not met.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex, $"Action '{action.Action}' condition could not be evaluated. Reason: {ex.Message}");
        }

        if (action.Schedule is { Mode: not ScheduleMode.Immediate } schedule)
        {
            try
            {
                var scheduledFor = ScheduleCalculator.Calculate(schedule, context, _timeProvider.GetUtcNow());

                return ActionResult.Scheduled(scheduledFor, $"Action '{action.Action}' scheduled for {scheduledFor:O}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ActionResult.Failed(ex, $"Action '{action.Action}' scheduling failed. Reason: {ex.Message}");
            }
        }

        if (!_catalog.TryGetAction(action.Action, out var registration))
        {
            var ex = new InvalidOperationException($"No action is registered under the name '{action.Action}'.");

            return ActionResult.Failed(ex, $"Action '{action.Action}' is not supported.");
        }

        try
        {
            var executor = _actionFactory.Create(registration);

            // Guards against actions that return null despite the non-nullable contract.
            ActionResult? result = await executor.ExecuteAsync(action, context, cancellationToken);

            return result ?? ActionResult.Failed(
                new InvalidOperationException($"Action '{action.Action}' returned no result."),
                $"Action '{action.Action}' execution failed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex, $"Action '{action.Action}' execution failed.");
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

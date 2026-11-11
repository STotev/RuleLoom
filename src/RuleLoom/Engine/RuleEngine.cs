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
/// <param name="options">How rules are run (e.g. skipping overdue actions); the defaults when not given. Read when the
/// engine is created.</param>
public sealed class RuleEngine(
    Catalog catalog,
    IActionFactory? actions = null,
    IContextProviderFactory? contextProviders = null,
    TimeProvider? timeProvider = null,
    RuleEngineOptions? options = null)
{
    private readonly Catalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly ConditionEvaluator _conditionEvaluator = new(catalog);
    private readonly IActionFactory _actionFactory = actions ?? new DirectActionFactory();
    private readonly IContextProviderFactory _contextProviders = contextProviders ?? new DirectContextProviderFactory();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly bool _skipOverdueActions = options?.SkipOverdueActions ?? false;
    private readonly TimeSpan _overdueGracePeriod = options?.OverdueGracePeriod ?? TimeSpan.Zero;

    /// <summary>
    /// Evaluates every rule for <paramref name="eventType"/> against the current and previous state and processes
    /// the actions of the rules that match: an event without envelope data (see <see cref="ExecuteAsync(RuleEvent, IEnumerable{RuleDefinition}, CancellationToken)"/>).
    /// </summary>
    /// <returns>One result per rule whose event type matched; failures are reported in the results, not thrown.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public Task<RuleEngineResult> ExecuteAsync(
        string eventType,
        object current,
        object? previous,
        IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new RuleEvent(eventType, current, previous), rules, cancellationToken);

    /// <summary>
    /// Evaluates every rule for the event's type against its current and previous state and processes the actions of
    /// the rules that match. Rules read the envelope with <c>$event.</c> paths; actions and operators read it from
    /// <see cref="EvaluationContext.Event"/>.
    /// </summary>
    /// <returns>One result per rule whose event type matched; failures are reported in the results, not thrown.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<RuleEngineResult> ExecuteAsync(
        RuleEvent @event,
        IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(rules);

        var eventType = @event.EventType;
        var ruleResults = new List<RuleExecutionResult>();
        var scheduledActions = new List<ScheduledAction>();

        var matchingRules = rules
            .Where(r => string.Equals(r.EventType, eventType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Priority)
            .ToList();

        var context = new EvaluationContext(@event, _catalog, _contextProviders);
        var subject = new Lazy<SubjectIdentity?>(() => SubjectOf(@event, context));

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
                {
                    scheduledActions.Add(new ScheduledAction(rule.Id, index, action.Action, result.ScheduledFor!.Value,
                        ScheduledAction.CreateKey(rule.Id, index, subject.Value)));
                }
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
    /// Runs an action the engine scheduled, now that it's due. Anything that runs later must pass its checks then: the
    /// rule must still be active and still have the action, and the rule's and the action's conditions are evaluated
    /// again. When the event's schema declares how to load its subject (<c>Subject(...).LoadedBy(...)</c>),
    /// <c>$current</c> is the subject loaded now and <c>$previous</c> the stored event's previous state, so
    /// <c>Status eq AwaitingPayment</c> means "still unpaid" and <c>changed Status</c> "still different from before the
    /// event"; without a loader, the checks see the stored event. Contexts load fresh. The rule's current version of the
    /// action runs, with <see cref="ExecuteAsync(RuleEvent, IEnumerable{RuleDefinition}, CancellationToken)"/>'s failure
    /// rules.
    /// </summary>
    /// <param name="scheduled">The scheduled action, as <see cref="RuleEngineResult.ScheduledActions"/> listed it.</param>
    /// <param name="storedEvent">The event that scheduled it, as it was then (e.g. stored with <see cref="RuleEvent.ToJson"/>).</param>
    /// <param name="rules">The app's current rules, as for <see cref="ExecuteAsync(RuleEvent, IEnumerable{RuleDefinition}, CancellationToken)"/>.</param>
    /// <param name="cancellationToken">Cancels loading and running.</param>
    /// <returns><see cref="ActionExecutionStatus.Executed"/>; <see cref="ActionExecutionStatus.Skipped"/> with the reason
    /// (the rule is gone or changed, it's overdue and <see cref="RuleEngineOptions.SkipOverdueActions"/> is on, a
    /// condition no longer holds, the subject no longer exists); or
    /// <see cref="ActionExecutionStatus.Failed"/>. Failures are reported in the result, not thrown.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<ActionResult> ExecuteScheduledAsync(
        ScheduledAction scheduled,
        RuleEvent storedEvent,
        IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduled);
        ArgumentNullException.ThrowIfNull(storedEvent);
        ArgumentNullException.ThrowIfNull(rules);

        var result = await RunScheduledAsync(scheduled, storedEvent, rules, cancellationToken);
        return result with { ActionName = scheduled.ActionName, ActionIndex = scheduled.ActionIndex };
    }

    private async Task<ActionResult> RunScheduledAsync(
        ScheduledAction scheduled,
        RuleEvent storedEvent,
        IEnumerable<RuleDefinition> rules,
        CancellationToken cancellationToken)
    {
        var rule = rules.FirstOrDefault(r => r.Id == scheduled.RuleId &&
                                             string.Equals(r.EventType, storedEvent.EventType, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
            return ActionResult.Skipped($"Rule '{scheduled.RuleId}' is no longer active.");

        var action = rule.Actions.ElementAtOrDefault(scheduled.ActionIndex);
        if (action is null || !string.Equals(action.Action, scheduled.ActionName, StringComparison.OrdinalIgnoreCase) ||
            action.Schedule is not { Mode: not ScheduleMode.Immediate })
        {
            return ActionResult.Skipped(
                $"Rule '{rule.Id}' no longer schedules action {scheduled.ActionIndex} ('{scheduled.ActionName}'): its actions changed.");
        }

        if (Overdue(action.Action, scheduled.ScheduledFor, _timeProvider.GetUtcNow()) is { } overdue)
            return overdue;

        EvaluationContext? context;
        try
        {
            context = await DueContextAsync(storedEvent, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex, $"The subject of action '{action.Action}' couldn't be loaded. Reason: {ex.Message}");
        }

        if (context is null)
            return ActionResult.Skipped($"Action '{action.Action}' skipped: its subject no longer exists (its loader found none).");

        await PrefetchContextsAsync(storedEvent.EventType, context, [rule], cancellationToken);

        try
        {
            if (!_conditionEvaluator.Evaluate(context, rule.Condition))
                return ActionResult.Skipped($"Action '{action.Action}' skipped: rule '{rule.Id}' no longer matches.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex, $"Rule '{rule.Id}' condition could not be evaluated. Reason: {ex.Message}");
        }

        return CheckCondition(action, context) ?? await RunAsync(action, context, cancellationToken);
    }

    // Skipped when the app skips overdue actions and this one is later than the grace period allows; otherwise null.
    private ActionResult? Overdue(string action, DateTimeOffset due, DateTimeOffset now)
    {
        var lateness = now - due;

        return _skipOverdueActions && lateness > _overdueGracePeriod
            ? ActionResult.Skipped($"Action '{action}' skipped: overdue by {Describe(lateness)} (grace period {Describe(_overdueGracePeriod)}).")
            : null;
    }

    // A duration in its two largest units, e.g. "3 hours 5 minutes" or "0 seconds".
    private static string Describe(TimeSpan span)
    {
        List<string> parts = [];
        foreach (var (amount, unit) in new[] { (span.Days, "day"), (span.Hours, "hour"), (span.Minutes, "minute"), (span.Seconds, "second") })
        {
            if (amount > 0)
                parts.Add($"{amount} {unit}{(amount == 1 ? "" : "s")}");
        }

        return parts.Count == 0 ? "0 seconds" : string.Join(' ', parts.Take(2));
    }

    /// <summary>
    /// The state a scheduled action is checked and run against: the subject loaded now, with the stored event's
    /// previous state and envelope, when the event's schema declares a loader; otherwise the stored event.
    /// </summary>
    /// <returns><c>null</c> when the loader finds no subject.</returns>
    private async Task<EvaluationContext?> DueContextAsync(RuleEvent storedEvent, CancellationToken cancellationToken)
    {
        var stored = new EvaluationContext(storedEvent, _catalog, _contextProviders);

        if (!_catalog.TryGetEvent(storedEvent.EventType, out var schema) || schema.Subject is not { Provider: { } provider } subject)
            return stored;

        if (!_catalog.TryGetContextProvider(provider, out var registration))
            throw new InvalidOperationException($"No context provider is registered under '{provider}' (the subject's loader).");

        var identity = subject.Identity;
        object? reference = identity.IsComposite
            ? identity.Parts.ToDictionary(part => part.Key, part => stored.Resolve(part.Value), StringComparer.OrdinalIgnoreCase)
            : stored.Resolve(identity.Path!);

        if (reference is null || reference is IReadOnlyDictionary<string, object?> parts && parts.Values.Any(value => value is null))
            throw new InvalidOperationException($"The stored event has no subject identity ({(identity.IsComposite ? string.Join(", ", identity.Parts.Values) : identity.Path)}).");

        var request = new ContextRequest(provider, storedEvent.EventType, storedEvent.Current, storedEvent.Previous)
        {
            Reference = reference,
            Event = storedEvent,
            TemporalRules = _catalog.TemporalRules
        };

        var current = await _contextProviders.Create(registration).GetAsync(request, cancellationToken);

        return current is null ? null : new EvaluationContext(storedEvent with { Current = current }, _catalog, _contextProviders);
    }

    /// <summary>
    /// The event's subject: read from its data when its schema declares the subject's identity, otherwise
    /// <see cref="RuleEvent.Subject"/>.
    /// </summary>
    private SubjectIdentity? SubjectOf(RuleEvent @event, EvaluationContext context)
    {
        if (!_catalog.TryGetEvent(@event.EventType, out var schema) || schema.Subject is not { Identity: var identity })
            return SubjectIdentity.From(@event.Subject);

        return identity.IsComposite
            ? SubjectIdentity.From(identity.Parts.Select(part => KeyValuePair.Create(part.Key, context.Resolve(part.Value))))
            : SubjectIdentity.From(context.Resolve(identity.Path!));
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
        if (CheckCondition(action, context) is { } notRun)
            return notRun;

        if (action.Schedule is { Mode: not ScheduleMode.Immediate } schedule)
        {
            var now = _timeProvider.GetUtcNow();
            DateTimeOffset scheduledFor;
            try
            {
                scheduledFor = ScheduleCalculator.Calculate(schedule, context, now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ActionResult.Failed(ex, $"Action '{action.Action}' scheduling failed. Reason: {ex.Message}");
            }

            if (scheduledFor >= now)
                return ActionResult.Scheduled(scheduledFor, $"Action '{action.Action}' scheduled for {scheduledFor:O}.");

            // Its due time already passed: it runs now, unless it's overdue and the app skips those.
            if (Overdue(action.Action, scheduledFor, now) is { } skipped)
                return skipped;
        }

        return await RunAsync(action, context, cancellationToken);
    }

    // An action's condition: Skipped when it doesn't hold, Failed when it can't be evaluated, null to go on.
    private ActionResult? CheckCondition(ActionDefinition action, EvaluationContext context)
    {
        try
        {
            return action.Condition is not null && !_conditionEvaluator.Evaluate(context, action.Condition)
                ? ActionResult.Skipped($"Action '{action.Action}' skipped because condition was not met.")
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex, $"Action '{action.Action}' condition could not be evaluated. Reason: {ex.Message}");
        }
    }

    // Creates the action through the factory and runs it; every failure becomes a Failed result.
    private async Task<ActionResult> RunAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken)
    {
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

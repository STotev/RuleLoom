using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Engine;

/// <summary>
/// What the engine reports besides its results: OpenTelemetry spans and metrics (named
/// <see cref="RuleLoomDiagnostics.Name"/>) and logs. Only names and ids from the catalog and the rules, statuses,
/// durations and error messages are written, never event state, context data or parameter values; the subject and the
/// event id go on spans and into Debug logs only.
/// </summary>
internal sealed partial class EngineTelemetry(ILogger logger)
{
    private static readonly string? Version = typeof(EngineTelemetry).Assembly.GetName().Version?.ToString();
    private static readonly ActivitySource Source = new(RuleLoomDiagnostics.Name, Version);
    private static readonly Meter Meter = new(RuleLoomDiagnostics.Name, Version);

    private static readonly Counter<long> Rules = Meter.CreateCounter<long>(
        "ruleloom.rules", description: "Rules evaluated, by event type and outcome (matched, not_matched, failed).");
    private static readonly Counter<long> Actions = Meter.CreateCounter<long>(
        "ruleloom.actions", description: "Actions processed, by event type, action and status.");
    private static readonly Histogram<double> ExecuteDuration = Meter.CreateHistogram<double>(
        "ruleloom.execute.duration", "s", "How long running an event's rules took.");
    private static readonly Histogram<double> ScheduledDuration = Meter.CreateHistogram<double>(
        "ruleloom.execute_scheduled.duration", "s", "How long running a scheduled action took.");
    private static readonly Histogram<double> SubjectDuration = Meter.CreateHistogram<double>(
        "ruleloom.subject.duration", "s", "How long loading a scheduled action's subject took, by provider and outcome.");

    /// <summary>A <c>ruleloom.execute</c> span for an event, or <c>null</c> when nothing listens.</summary>
    public static Activity? StartExecute(RuleEvent @event, Func<string?> subject) =>
        Start("ruleloom.execute", @event, subject);

    /// <summary>A <c>ruleloom.execute_scheduled</c> span for a scheduled action.</summary>
    public static Activity? StartScheduled(RuleEvent @event, ScheduledAction scheduled)
    {
        var activity = Start("ruleloom.execute_scheduled", @event, () => @event.Subject);
        activity?.SetTag("ruleloom.rule_id", scheduled.RuleId);
        activity?.SetTag("ruleloom.action", scheduled.ActionName);
        activity?.SetTag("ruleloom.action_index", scheduled.ActionIndex);
        return activity;
    }

    /// <summary>Whether Debug logs are written, which name the subject (worked out only then).</summary>
    public bool Detailed => logger.IsEnabled(LogLevel.Debug);

    /// <summary>A <c>ruleloom.action</c> span for an action that runs.</summary>
    public static Activity? StartAction(string action, string ruleId)
    {
        var activity = Source.StartActivity("ruleloom.action");
        activity?.SetTag("ruleloom.action", action);
        activity?.SetTag("ruleloom.rule_id", ruleId);
        return activity;
    }

    /// <summary>Ends an action's span with its result.</summary>
    public static void EndAction(Activity? activity, ActionResult result)
    {
        activity?.SetTag("ruleloom.status", result.Status.ToString());
        if (result is { Status: ActionExecutionStatus.Failed, Error: { } error })
            Fail(activity, error);
    }

    /// <summary>A <c>ruleloom.context</c> span for loading a scheduled action's subject.</summary>
    public static async Task<object?> LoadSubjectAsync(string provider, Func<Task<object?>> load)
    {
        using var activity = Source.StartActivity("ruleloom.context");
        activity?.SetTag("ruleloom.provider", provider);
        activity?.SetTag("ruleloom.subject_load", true);
        var started = Stopwatch.GetTimestamp();
        var outcome = "failure";

        try
        {
            var loaded = await load().ConfigureAwait(false);
            outcome = "success";
            return loaded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(activity, ex);
            throw;
        }
        finally
        {
            SubjectDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("ruleloom.provider", provider), new KeyValuePair<string, object?>("ruleloom.outcome", outcome));
        }
    }

    public void ContextFailed(string eventType, string context, Exception error) =>
        LogContextFailed(logger, eventType, context, error.Message);

    public void OverdueSkipped(string eventType, string ruleId, string action, string reason) =>
        LogOverdueSkipped(logger, eventType, ruleId, action, reason);

    /// <summary>Counts and logs a rule's outcome; a failure is a span event too.</summary>
    public void RuleOutcome(Activity? activity, RuleEvent @event, RuleExecutionResult rule, string? subject)
    {
        Rules.Add(1, new KeyValuePair<string, object?>("ruleloom.event_type", @event.EventType),
            new KeyValuePair<string, object?>("ruleloom.outcome", Outcome(rule.Status)));

        if (rule.Status == RuleExecutionStatus.Failed)
        {
            activity?.AddEvent(new ActivityEvent("ruleloom.rule_failed", tags: new ActivityTagsCollection
            {
                ["ruleloom.rule_id"] = rule.RuleId,
                ["exception.message"] = rule.Error?.Message
            }));
            LogRuleFailed(logger, @event.EventType, rule.RuleId, rule.Error?.Message);
        }

        LogRuleOutcome(logger, @event.EventType, rule.RuleId, rule.Status, subject, @event.EventId);
    }

    /// <summary>Counts and logs an action's outcome.</summary>
    public void ActionOutcome(RuleEvent @event, string ruleId, ActionResult result, string? subject)
    {
        Actions.Add(1, new KeyValuePair<string, object?>("ruleloom.event_type", @event.EventType),
            new KeyValuePair<string, object?>("ruleloom.action", result.ActionName),
            new KeyValuePair<string, object?>("ruleloom.status", result.Status.ToString()));

        if (result.Status == ActionExecutionStatus.Failed)
            LogActionFailed(logger, @event.EventType, ruleId, result.ActionName, result.ActionIndex, result.Error?.Message ?? result.Message);

        LogActionOutcome(logger, @event.EventType, ruleId, result.ActionName, result.ActionIndex, result.Status, result.Message, subject, @event.EventId);
    }

    /// <summary>Ends an event's span with its counts, and records its duration.</summary>
    public static void EndExecute(Activity? activity, RuleEvent @event, RuleEngineResult result, long started)
    {
        ExecuteDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("ruleloom.event_type", @event.EventType));

        if (activity is null)
            return;

        foreach (var outcome in result.Rules.GroupBy(r => r.Status))
            activity.SetTag($"ruleloom.rules.{Outcome(outcome.Key)}", outcome.Count());

        foreach (var status in result.Rules.SelectMany(r => r.ActionResults).GroupBy(a => a.Status))
            activity.SetTag($"ruleloom.actions.{status.Key.ToString().ToLowerInvariant()}", status.Count());

        if (result.HasFailures)
            activity.SetStatus(ActivityStatusCode.Error, "Some rules or actions failed.");
    }

    /// <summary>Ends a scheduled action's span with its status, and records its duration.</summary>
    public void EndScheduled(Activity? activity, RuleEvent @event, ScheduledAction scheduled, ActionResult result, long started)
    {
        ScheduledDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("ruleloom.event_type", @event.EventType));

        activity?.SetTag("ruleloom.status", result.Status.ToString());
        if (result.Status == ActionExecutionStatus.Skipped)
            activity?.SetTag("ruleloom.reason", result.Message);
        if (result is { Status: ActionExecutionStatus.Failed, Error: { } error })
            Fail(activity, error);

        ActionOutcome(@event, scheduled.RuleId, result, Detailed ? @event.Subject : null);
    }

    private static Activity? Start(string name, RuleEvent @event, Func<string?> subject)
    {
        var activity = Source.StartActivity(name);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("ruleloom.event_type", @event.EventType);
            activity.SetTag("ruleloom.subject", subject());
            activity.SetTag("ruleloom.event_id", @event.EventId);
        }

        return activity;
    }

    private static void Fail(Activity? activity, Exception error)
    {
        activity?.SetStatus(ActivityStatusCode.Error, error.Message);
        activity?.AddException(error);
    }

    private static string Outcome(RuleExecutionStatus status) => status switch
    {
        RuleExecutionStatus.Matched => "matched",
        RuleExecutionStatus.NotMatched => "not_matched",
        _ => "failed"
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Context '{Context}' failed to load for {EventType}: {Error}")]
    private static partial void LogContextFailed(ILogger logger, string eventType, string context, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rule '{RuleId}' failed for {EventType}: {Error}")]
    private static partial void LogRuleFailed(ILogger logger, string eventType, string ruleId, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action '{Action}' ({ActionIndex}) of rule '{RuleId}' failed for {EventType}: {Error}")]
    private static partial void LogActionFailed(ILogger logger, string eventType, string ruleId, string action, int actionIndex, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action '{Action}' of rule '{RuleId}' for {EventType} was skipped: {Reason}")]
    private static partial void LogOverdueSkipped(ILogger logger, string eventType, string ruleId, string action, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rule '{RuleId}' for {EventType}: {Status} (subject {Subject}, event {EventId})")]
    private static partial void LogRuleOutcome(ILogger logger, string eventType, string ruleId, RuleExecutionStatus status, string? subject, string? eventId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Action '{Action}' ({ActionIndex}) of rule '{RuleId}' for {EventType}: {Status} {Message} (subject {Subject}, event {EventId})")]
    private static partial void LogActionOutcome(ILogger logger, string eventType, string ruleId, string action, int actionIndex,
        ActionExecutionStatus status, string? message, string? subject, string? eventId);
}

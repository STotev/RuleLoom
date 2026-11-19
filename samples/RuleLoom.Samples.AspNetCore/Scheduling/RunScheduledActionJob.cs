using Quartz;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
using RuleLoom.Samples.AspNetCore.Demo;
using RuleLoom.Samples.AspNetCore.Rules;

namespace RuleLoom.Samples.AspNetCore.Scheduling;

/// <summary>
/// Runs a scheduled action when it's due, with one call: <see cref="RuleEngine.ExecuteScheduledAsync"/>, given the event
/// that scheduled it and the current rules. RuleLoom loads the order as it is now, checks the rule and the action again,
/// and runs the rule's current version of the action, or skips it with the reason.
/// </summary>
public sealed class RunScheduledActionJob(RuleEngine engine, RuleStore ruleStore, ActivityFeed activity, TimeProvider time,
    ILogger<RunScheduledActionJob> logger) : IJob
{
    public const string ScheduledActionKey = "scheduledAction";
    public const string EventKey = "event";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var scheduled = ScheduledAction.Parse(context.MergedJobDataMap.GetString(ScheduledActionKey)!);
        var @event = RuleEvent.Parse(context.MergedJobDataMap.GetString(EventKey)!);

        var rules = await ruleStore.LoadAsync(cancellationToken);
        var result = await engine.ExecuteScheduledAsync(scheduled, @event, rules.Valid, cancellationToken);

        var entry = activity.Add(ActivityEntry.ForScheduledRun(@event, scheduled, rules.Valid, result, time.GetUtcNow()));
        logger.LogInformation("{Rule} for {Order}: {Action} {Status} {Reason}",
            entry.Rules[0].RuleName, entry.Order, scheduled.ActionName, result.Status, result.Message ?? result.Error?.Message);
    }
}

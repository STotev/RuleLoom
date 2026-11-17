using RuleLoom.Abstractions;
using RuleLoom.Engine;
using RuleLoom.Samples.AspNetCore.Demo;
using RuleLoom.Samples.AspNetCore.Scheduling;

namespace RuleLoom.Samples.AspNetCore.Rules;

/// <summary>
/// Where the shop hands an event to RuleLoom: runs the current rules, gives the scheduled actions to Quartz, and records
/// what happened for the demo page.
/// </summary>
public sealed class RuleRunner(RuleEngine engine, RuleStore ruleStore, ActionScheduler scheduler, ActivityFeed activity, TimeProvider time)
{
    public async Task<ActivityEntry> RunAsync(RuleEvent @event, CancellationToken cancellationToken)
    {
        @event = @event with { EventId = Guid.NewGuid().ToString("N"), OccurredAt = time.GetUtcNow() };

        var rules = await ruleStore.LoadAsync(cancellationToken);
        var result = await engine.ExecuteAsync(@event, rules.Valid, cancellationToken);
        await scheduler.ScheduleAsync(@event, result.ScheduledActions, cancellationToken);

        return activity.Add(ActivityEntry.ForEvent(@event, result, time.GetUtcNow()));
    }
}

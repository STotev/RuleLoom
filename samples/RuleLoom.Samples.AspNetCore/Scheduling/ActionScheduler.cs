using Quartz;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Samples.AspNetCore.Scheduling;

/// <summary>
/// Hands the engine's scheduled actions to Quartz: one job per action, fired at its <c>ScheduledFor</c>.
/// </summary>
/// <remarks>
/// Job and trigger are keyed by <see cref="ScheduledAction.Key"/> (<c>{rule}:{action index}:{order number}</c>), and a
/// pending job with the same key is replaced, so the same order doesn't get the same reminder twice. The job data is
/// text (<see cref="ScheduledAction.ToJson"/>, <see cref="RuleEvent.ToJson"/>), so a persistent Quartz store works too.
/// </remarks>
public sealed class ActionScheduler(ISchedulerFactory schedulerFactory)
{
    public async Task ScheduleAsync(RuleEvent @event, IReadOnlyList<ScheduledAction> scheduledActions, CancellationToken cancellationToken)
    {
        if (scheduledActions.Count == 0)
            return;

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        foreach (var scheduled in scheduledActions)
        {
            // Order events declare their subject (the order number), so every key names the order.
            var key = scheduled.Key!;

            var job = JobBuilder.Create<RunScheduledActionJob>().WithIdentity(key).Build();
            job.JobDataMap[RunScheduledActionJob.ScheduledActionKey] = scheduled.ToJson();
            job.JobDataMap[RunScheduledActionJob.EventKey] = @event.ToJson();

            // The trigger needs the same key too: replacing a job keeps its old triggers under other keys.
            var trigger = TriggerBuilder.Create().WithIdentity(key).StartAt(scheduled.ScheduledFor).Build();

            await scheduler.ScheduleJob(job, trigger, ScheduleJobOptions.Replacing, cancellationToken);
        }
    }
}

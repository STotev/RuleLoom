using Quartz;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Samples.AspNetCore;

/// <summary>
/// Hands the engine's scheduled actions to Quartz: one job per action, fired at its <c>ScheduledFor</c>.
/// </summary>
/// <remarks>
/// Job and trigger keys are <c>{tenant}/{entity}/{rule}/{action index}</c>, and an existing job with the same key is
/// replaced, so the same event sent twice doesn't schedule the action twice. The job data holds the objects themselves,
/// which only works with Quartz's in-memory store.
/// </remarks>
public sealed class ActionScheduler(ISchedulerFactory schedulerFactory)
{
    public async Task<IReadOnlyList<ScheduledJob>> ScheduleAsync(
        EventRequest request,
        IReadOnlyList<ScheduledAction> scheduledActions,
        CancellationToken cancellationToken)
    {
        if (scheduledActions.Count == 0)
            return [];

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var jobs = new List<ScheduledJob>(scheduledActions.Count);

        foreach (var scheduled in scheduledActions)
        {
            var key = new JobKey($"{request.TenantId}/{request.EntityId}/{scheduled.RuleId}/{scheduled.Result.ActionIndex}");
            var scheduledFor = scheduled.Result.ScheduledFor!.Value;

            var job = JobBuilder.Create<RunScheduledActionJob>()
                .WithIdentity(key)
                .Build();

            job.JobDataMap[RunScheduledActionJob.TenantIdKey] = request.TenantId;
            job.JobDataMap[RunScheduledActionJob.ScheduledActionKey] = scheduled;
            job.JobDataMap[RunScheduledActionJob.CurrentKey] = request.Current;
            job.JobDataMap[RunScheduledActionJob.PreviousKey] = request.Previous;

            // The trigger needs the same key too: replacing a job keeps its old triggers under other keys.
            var trigger = TriggerBuilder.Create()
                .WithIdentity(key.Name)
                .StartAt(scheduledFor)
                .Build();

            await scheduler.ScheduleJob(job, trigger, ScheduleJobOptions.Replacing, cancellationToken);
            jobs.Add(new ScheduledJob(key.Name, scheduledFor));
        }

        return jobs;
    }
}

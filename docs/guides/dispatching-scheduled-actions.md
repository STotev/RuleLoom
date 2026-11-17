# Dispatching Scheduled Actions

An action with a `schedule` isn't run when its event is processed. `ExecuteAsync` returns it in
`result.ScheduledActions` with its due time; your scheduler (Quartz.NET, Hangfire, a polling job) holds it until then
and hands it back to RuleLoom with **one call**. RuleLoom checks it again when it's due and runs it, or tells you why
not. The [ASP.NET Core sample](../../samples/RuleLoom.Samples.AspNetCore/) does all of this with Quartz (one shop, so its keys
have no tenant prefix).

## The loop

```csharp
// 1. When an event arrives:
var result = await engine.ExecuteAsync(@event, rules, ct);

foreach (var scheduled in result.ScheduledActions)
    await scheduler.ScheduleAsync(key: $"{tenantId}/{scheduled.Key}", due: scheduled.ScheduledFor,
        data: (tenantId, scheduled.ToJson(), @event.ToJson()));

// 2. When a job is due:
var result = await engine.ExecuteScheduledAsync(ScheduledAction.Parse(scheduledJson), RuleEvent.Parse(eventJson),
    await LoadRulesAsync(tenantId, ct), ct);   // Executed, Skipped (with the reason) or Failed
```

- **Store text.** `ScheduledAction.ToJson()` and `RuleEvent.ToJson()` are what a persistent job store keeps;
  `ScheduledAction.Parse` and `RuleEvent.Parse` read them back. A scheduled action is a reference ("rule X's action N,
  due then"), not a copy: when it's due, the rule's **current** version runs, so edits apply to pending actions.
- **Pass your current rules**, as for `ExecuteAsync`. A rule that was deleted or disabled, or edited so it no longer
  schedules that action, skips it.

## Keys: one pending job per action and subject

`ScheduledAction.Key` is `{ruleId}:{actionIndex}:{subject}`, e.g. `payment-reminder:0:SO-1001`. Use it as the job's id
so the same event delivered twice replaces its job instead of adding a second one.

- **The subject** is the identity the event's schema declares (`Subject(o => o.Number)`), or else the event's
  `RuleEvent.Subject`. Without either, `Key` is `null`: the action can still be scheduled, but not replaced by key.
- **Prefix your tenant** (or anything else that separates rule sets) when rule ids repeat: `acme-shop/payment-reminder:0:SO-1001`.
- **Quartz: key the trigger too.** Replacing a job (`ScheduleJobOptions.Replacing`) keeps old triggers that have other
  keys, so the action would run twice. Give the trigger the job's key:

```csharp
var key = new JobKey($"{tenantId}/{scheduled.Key}");
var job = JobBuilder.Create<RunScheduledActionJob>().WithIdentity(key).Build();
job.JobDataMap["tenantId"] = tenantId;
job.JobDataMap["scheduledAction"] = scheduled.ToJson();
job.JobDataMap["event"] = @event.ToJson();

var trigger = TriggerBuilder.Create().WithIdentity(key.Name).StartAt(scheduled.ScheduledFor).Build();
await scheduler.ScheduleJob(job, trigger, ScheduleJobOptions.Replacing, ct);
```

```csharp
public sealed class RunScheduledActionJob(RuleEngine engine, MyRuleStore rules) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct)
    {
        var data = context.MergedJobDataMap;
        var result = await engine.ExecuteScheduledAsync(
            ScheduledAction.Parse(data.GetString("scheduledAction")!),
            RuleEvent.Parse(data.GetString("event")!),
            await rules.LoadAsync(data.GetString("tenantId")!, ct), ct);
    }
}
```

**Hangfire** schedules a method call and gives each job its own id, so keep your key → job id mapping where you keep
state, and delete the pending job before scheduling its replacement:

```csharp
if (await jobIds.FindAsync(key, ct) is { } previous)
    BackgroundJob.Delete(previous);

var jobId = BackgroundJob.Schedule<ScheduledActionRunner>(
    runner => runner.RunAsync(tenantId, scheduled.ToJson(), @event.ToJson(), CancellationToken.None), scheduled.ScheduledFor);
await jobIds.SaveAsync(key, jobId, ct);
```

## Checked again when due

Anything that runs later must pass its checks then. `ExecuteScheduledAsync` evaluates the rule's condition and the
action's condition again. What they read depends on whether the event's schema says how to load its subject:

```csharp
public sealed class OrderPlacedSchema : EventSchema<Order>
{
    public OrderPlacedSchema()
    {
        Event("OrderPlaced");
        Subject(o => o.Number).LoadedBy("order");   // a ContextProvider<Order> registered as "order"
    }
}
```

- **With a loader**, the provider loads the order as it is now (given its number as the reference; the event, with its
  metadata such as a tenant id, is on the request). `$current` is that order and `$previous` the event's previous state:

| Condition | When the action is due |
|---|---|
| `Status eq AwaitingPayment` | the order is still unpaid |
| `changed Status` | the status still differs from before the event (a change that was undone skips the action) |
| `Price lt $previous.Price` | the price is still below what it was before the event |

  If the provider finds nothing (the order was deleted), the action is skipped. `RuleValidator` checks that the paths
  a scheduled action reads are fields of the loaded order too.
- **Without a loader**, the checks see the stored event as it was, so only edits to the rule can change the outcome.

## Late and past due times

- A due time that has already passed when the event is processed runs the action immediately (`Executed`).
- Schedulers don't fire exactly on time, and can be down for a while. To skip actions that are too late to be useful,
  skip overdue actions with a grace period that fits your scheduler:

```csharp
services.AddRuleLoom(r => r.Configure(o =>
{
    o.SkipOverdueActions = true;
    o.OverdueGracePeriod = TimeSpan.FromMinutes(1);
}));
```

  An action later than that is `Skipped` ("overdue by 3 hours (grace period 1 minute)"), whether the event was late or
  your scheduler ran it late.

## Watching it

`ruleloom.execute_scheduled` spans and Debug logs carry the rule, the action, the status and the reason a run was
skipped; see [observability](observability.md).

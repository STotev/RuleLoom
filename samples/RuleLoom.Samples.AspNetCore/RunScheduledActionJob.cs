using Quartz;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;

namespace RuleLoom.Samples.AspNetCore;

/// <summary>
/// Runs a scheduled action when it's due: one call to <see cref="RuleEngine.ExecuteScheduledAsync"/> with the event
/// that scheduled it and the tenant's current rules. RuleLoom checks the rule and the action again (and skips them if
/// the rule is gone, was edited, or no longer matches) and runs the rule's current version of the action.
/// </summary>
public sealed class RunScheduledActionJob(RuleEngine engine, TenantRuleStore ruleStore, ILogger<RunScheduledActionJob> logger) : IJob
{
    public const string TenantIdKey = "tenantId";
    public const string ScheduledActionKey = "scheduledAction";
    public const string EventKey = "event";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var data = context.MergedJobDataMap;
        var tenantId = (string)data[TenantIdKey]!;
        var scheduled = (ScheduledAction)data[ScheduledActionKey]!;
        var @event = (RuleEvent)data[EventKey]!;

        var rules = await ruleStore.LoadAsync(tenantId, cancellationToken);
        var result = await engine.ExecuteScheduledAsync(scheduled, @event, rules?.Valid ?? [], cancellationToken);

        logger.LogInformation(
            "Scheduled action '{Action}' of rule '{Rule}' for tenant '{Tenant}' ran: {Status} {Message}",
            scheduled.ActionName, scheduled.RuleId, tenantId, result.Status, result.Message ?? result.Error?.Message);
    }
}

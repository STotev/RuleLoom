using Quartz;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Samples.RulesService;

/// <summary>
/// Runs a scheduled action when it's due, with the state of the event that scheduled it. Rule and action conditions
/// aren't checked again.
/// </summary>
/// <remarks>
/// RuleLoom has no entry point for running one action later, so this repeats what the engine does for an immediate
/// action: find the executor by name, run it, and turn exceptions into a failed result.
/// </remarks>
public sealed class RunScheduledActionJob(IActionFactory actionFactory, ILogger<RunScheduledActionJob> logger) : IJob
{
    public const string TenantIdKey = "tenantId";
    public const string ScheduledActionKey = "scheduledAction";
    public const string CurrentKey = "current";
    public const string PreviousKey = "previous";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var data = context.MergedJobDataMap;
        var tenantId = (string)data[TenantIdKey]!;
        var scheduled = (ScheduledAction)data[ScheduledActionKey]!;
        var state = new EvaluationContext(data[CurrentKey], data[PreviousKey]);

        var result = await RunAsync(scheduled.Action, state, cancellationToken);

        logger.LogInformation(
            "Scheduled action '{Action}' of rule '{Rule}' for tenant '{Tenant}' ran: {Status} {Message}",
            scheduled.Action.Action, scheduled.RuleName, tenantId, result.Status, result.Message ?? result.Error?.Message);
    }

    private async Task<ActionResult> RunAsync(ActionDefinition action, EvaluationContext state, CancellationToken cancellationToken)
    {
        if (!actionFactory.TryGetAction(action.Action, out var executor))
            return ActionResult.Failed(new InvalidOperationException($"No action named '{action.Action}' is registered."));

        try
        {
            return await executor!.ExecuteAsync(action, state, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex);
        }
    }
}

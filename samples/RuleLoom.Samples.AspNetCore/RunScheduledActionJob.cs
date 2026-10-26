using Quartz;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Samples.AspNetCore;

/// <summary>
/// Runs a scheduled action when it's due, with the state of the event that scheduled it and its contexts as they are
/// now (loaded when the action reads them, e.g. a <c>$path</c> parameter into <c>@customer</c>). Rule and action
/// conditions aren't checked again.
/// </summary>
/// <remarks>
/// RuleLoom has no entry point for running one action later, so this repeats what the engine does for an immediate
/// action: build the evaluation context, find the action in the catalog by name, create it, run it, and turn exceptions
/// into a failed result.
/// </remarks>
public sealed class RunScheduledActionJob(
    Catalog catalog,
    IActionFactory actionFactory,
    IContextProviderFactory contextProviders,
    ILogger<RunScheduledActionJob> logger) : IJob
{
    public const string TenantIdKey = "tenantId";
    public const string EventTypeKey = "eventType";
    public const string ScheduledActionKey = "scheduledAction";
    public const string CurrentKey = "current";
    public const string PreviousKey = "previous";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var data = context.MergedJobDataMap;
        var tenantId = (string)data[TenantIdKey]!;
        var scheduled = (ScheduledAction)data[ScheduledActionKey]!;
        var state = new EvaluationContext(data[CurrentKey], data[PreviousKey], (string)data[EventTypeKey]!, catalog, contextProviders);

        var result = await RunAsync(scheduled.Action, state, cancellationToken);

        logger.LogInformation(
            "Scheduled action '{Action}' of rule '{Rule}' for tenant '{Tenant}' ran: {Status} {Message}",
            scheduled.Action.Action, scheduled.RuleName, tenantId, result.Status, result.Message ?? result.Error?.Message);
    }

    private async Task<ActionResult> RunAsync(ActionDefinition action, EvaluationContext state, CancellationToken cancellationToken)
    {
        if (!catalog.TryGetAction(action.Action, out var registration))
            return ActionResult.Failed(new InvalidOperationException($"No action named '{action.Action}' is registered."));

        try
        {
            return await actionFactory.Create(registration).ExecuteAsync(action, state, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Failed(ex);
        }
    }
}

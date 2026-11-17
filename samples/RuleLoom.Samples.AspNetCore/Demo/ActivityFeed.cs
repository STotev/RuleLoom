using System.Text.Json;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Samples.AspNetCore.Orders;

namespace RuleLoom.Samples.AspNetCore.Demo;

/// <summary>
/// What happened, for the demo page: each event with its rules' outcomes, and each scheduled action run when due.
/// Keeps the latest 100 entries in memory; an app would use its logs and traces instead.
/// </summary>
public sealed class ActivityFeed
{
    private readonly LinkedList<ActivityEntry> _entries = [];

    public ActivityEntry Add(ActivityEntry entry)
    {
        lock (_entries)
        {
            _entries.AddFirst(entry);
            if (_entries.Count > 100)
                _entries.RemoveLast();
        }

        return entry;
    }

    public IReadOnlyList<ActivityEntry> Latest()
    {
        lock (_entries)
            return _entries.ToList();
    }
}

/// <param name="Kind"><c>event</c> (an event's rules ran) or <c>scheduled</c> (a scheduled action came due).</param>
public sealed record ActivityEntry(DateTimeOffset At, string Kind, string Title, string? Order, IReadOnlyList<RuleOutcome> Rules)
{
    public static ActivityEntry ForEvent(RuleEvent @event, RuleEngineResult result, DateTimeOffset at) =>
        new(at, "event", @event.EventType, OrderNumberOf(@event), result.Rules.Select(rule => new RuleOutcome(
            rule.RuleId, rule.RuleName, rule.Status.ToString(), rule.Error?.Message,
            rule.ActionResults.Select(ActionOutcome.From).ToList())).ToList());

    public static ActivityEntry ForScheduledRun(RuleEvent @event, ScheduledAction scheduled, IReadOnlyList<RuleDefinition> rules,
        ActionResult result, DateTimeOffset at)
    {
        var ruleName = rules.FirstOrDefault(r => r.Id == scheduled.RuleId)?.Name ?? scheduled.RuleId;
        var outcome = new RuleOutcome(scheduled.RuleId, ruleName, "Due", null, [ActionOutcome.From(result)]);

        return new(at, "scheduled", $"Scheduled by {@event.EventType}", OrderNumberOf(@event), [outcome]);
    }

    // The event raised in-process carries an Order; the one stored with a scheduled action is JSON.
    private static string? OrderNumberOf(RuleEvent @event) => @event.Current switch
    {
        Order order => order.Number,
        JsonElement json => json.EnumerateObject().FirstOrDefault(p => p.NameEquals("number")).Value.GetString(),
        _ => null
    };
}

public sealed record RuleOutcome(string RuleId, string RuleName, string Status, string? Error, IReadOnlyList<ActionOutcome> Actions);

public sealed record ActionOutcome(string Action, int Index, string Status, string? Message, DateTimeOffset? ScheduledFor)
{
    public static ActionOutcome From(ActionResult action) => new(action.ActionName, action.ActionIndex, action.Status.ToString(),
        action.Status == ActionExecutionStatus.Scheduled ? null : action.Message ?? action.Error?.Message, action.ScheduledFor);
}

using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Samples.RulesService;

/// <summary>
/// What happened to an event: the engine result (errors as messages, since exceptions don't serialize usefully),
/// the tenant's rules that were skipped as invalid, and the jobs handed to Quartz.
/// </summary>
public sealed record EventResponse(
    IReadOnlyList<RuleResponse> Rules,
    IReadOnlyList<InvalidRule> InvalidRules,
    IReadOnlyList<ScheduledJob> ScheduledJobs)
{
    public static EventResponse From(RuleEngineResult result, IReadOnlyList<InvalidRule> invalidRules, IReadOnlyList<ScheduledJob> jobs) =>
        new(result.Rules.Select(RuleResponse.From).ToList(), invalidRules, jobs);
}

public sealed record RuleResponse(string RuleId, string RuleName, string Status, string? Error, IReadOnlyList<ActionResponse> Actions)
{
    public static RuleResponse From(RuleExecutionResult rule) => new(
        rule.RuleId,
        rule.RuleName,
        rule.Status.ToString(),
        rule.Error?.Message,
        rule.ActionResults.Select(action => new ActionResponse(
            action.ActionName,
            action.ActionIndex,
            action.Status.ToString(),
            action.Message ?? action.Error?.Message,
            action.ScheduledFor)).ToList());
}

public sealed record ActionResponse(string Action, int Index, string Status, string? Message, DateTimeOffset? ScheduledFor);

public sealed record InvalidRule(string File, string? Rule, IReadOnlyList<string> Errors);

public sealed record ScheduledJob(string JobKey, DateTimeOffset ScheduledFor);

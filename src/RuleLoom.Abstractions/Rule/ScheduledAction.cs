using RuleLoom.Abstractions.Action;

namespace RuleLoom.Abstractions.Rule;

/// <summary>
/// An action the engine scheduled instead of executing, with everything needed to dispatch it later.
/// </summary>
/// <param name="RuleId">The <see cref="RuleDefinition.Id"/> of the rule the action belongs to.</param>
/// <param name="RuleName">The <see cref="RuleDefinition.Name"/> of the rule the action belongs to.</param>
/// <param name="Action">The action's definition, including its type and parameters.</param>
/// <param name="Result">The <see cref="ActionExecutionStatus.Scheduled"/> result, including <see cref="ActionResult.ScheduledFor"/>.</param>
public sealed record ScheduledAction(string RuleId, string RuleName, ActionDefinition Action, ActionResult Result);

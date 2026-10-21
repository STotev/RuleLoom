using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;

namespace RuleLoom.Validation;

/// <summary>
/// One kind of check, given each part of a rule by <see cref="RuleWalker"/>; it overrides the parts it checks.
/// </summary>
internal abstract class RuleCheck
{
    public virtual void Rule(RuleDefinition rule, ValidationProblems problems)
    {
    }

    /// <summary>A rule's or an action's condition, which must be an operator.</summary>
    public virtual void RootCondition(ConditionNode? condition, string location, string description, ValidationProblems problems)
    {
    }

    /// <summary>An operator node, with the registered operator of that name (<c>null</c> when there is none).</summary>
    public virtual void Operator(OperatorCondition condition, IConditionOperator? @operator, string location, ValidationProblems problems)
    {
    }

    /// <summary>
    /// An operator node again, after its operands were visited, with the path operands (<c>null</c> for other operands).
    /// </summary>
    public virtual void Operands(OperatorCondition condition, IConditionOperator? @operator, IReadOnlyList<PathNode?> paths,
        string location, ValidationProblems problems)
    {
    }

    public virtual void Path(PathNode path, ValidationProblems problems)
    {
    }

    public virtual void Action(ActionDefinition action, string location, ValidationProblems problems)
    {
    }

    /// <summary>An action's schedule; <c>location</c> is the action's, the schedule is at <c>{location}.schedule</c>.</summary>
    public virtual void Schedule(ScheduleMetadata schedule, string location, ValidationProblems problems)
    {
    }
}

/// <summary>
/// A path in a rule (an operand or a schedule's reference path), parsed once.
/// </summary>
/// <param name="Text">The path as written.</param>
/// <param name="Parsed">The parsed path, or <c>null</c> when it isn't valid.</param>
/// <param name="Error">Why it isn't valid.</param>
/// <param name="Location">Its JSONPath in the rule.</param>
/// <param name="Parent">The registered operator the path is an operand of, if any.</param>
internal sealed record PathNode(string Text, RulePath? Parsed, string? Error, string Location, IConditionOperator? Parent);

/// <summary>
/// The errors and warnings found so far, in document order.
/// </summary>
internal sealed class ValidationProblems
{
    public List<RuleValidationError> Errors { get; } = [];
    public List<RuleValidationError> Warnings { get; } = [];

    public void Error(string location, string message) => Errors.Add(new(location, message));
    public void Warning(string location, string message) => Warnings.Add(new(location, message));

    public RuleValidationResult ToResult() => new(Errors) { Warnings = Warnings };
}

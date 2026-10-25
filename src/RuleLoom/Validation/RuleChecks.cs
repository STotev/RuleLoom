using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;

namespace RuleLoom.Validation;

/// <summary>A rule's id, name and event type are required.</summary>
internal sealed class RequiredFieldsCheck : RuleCheck
{
    public override void Rule(RuleDefinition rule, ValidationProblems problems)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
            problems.Error("$.id", "'id' must be a non-empty string.");

        if (string.IsNullOrWhiteSpace(rule.Name))
            problems.Error("$.name", "'name' must be a non-empty string.");

        if (string.IsNullOrWhiteSpace(rule.EventType))
            problems.Error("$.eventType", "'eventType' must be a non-empty string.");
    }
}

/// <summary>A rule's event type must be in the catalog.</summary>
internal sealed class EventTypeCheck(Catalog catalog) : RuleCheck
{
    public override void Rule(RuleDefinition rule, ValidationProblems problems)
    {
        if (!string.IsNullOrWhiteSpace(rule.EventType) && !catalog.TryGetEvent(rule.EventType, out _))
            problems.Error("$.eventType", $"Event type '{rule.EventType}' isn't in the catalog.");
    }
}

/// <summary>A rule's and an action's condition must be an operator.</summary>
internal sealed class RootConditionCheck : RuleCheck
{
    public override void RootCondition(ConditionNode? condition, string location, string description, ValidationProblems problems)
    {
        if (condition is not OperatorCondition)
            problems.Error(location, $"{description} must be an operator, not {condition?.GetType().Name ?? "null"}.");
    }
}

/// <summary>
/// Operators must be registered and get an operand count within their limits; then each operator validates its own
/// operands (<see cref="IConditionOperator.ValidateOperands"/>), with the schema fields of path operands when there are
/// schemas.
/// </summary>
internal sealed class OperatorCheck(SchemaPaths? schemaPaths) : RuleCheck
{
    public override void Operator(OperatorCondition condition, IConditionOperator? @operator, string location, ValidationProblems problems)
    {
        if (@operator is null)
        {
            problems.Error($"{location}.operator", $"Unknown operator '{condition.Operator}'.");
        }
        else if (condition.Operands.Count < @operator.MinOperands)
        {
            problems.Error($"{location}.operands",
                $"Operator '{condition.Operator}' requires at least {@operator.MinOperands} operand(s), but has {condition.Operands.Count}.");
        }
        else if (condition.Operands.Count > @operator.MaxOperands)
        {
            problems.Error($"{location}.operands",
                $"Operator '{condition.Operator}' allows at most {@operator.MaxOperands} operand(s), but has {condition.Operands.Count}.");
        }
    }

    // After the operands, so their paths have been reported (and resolved on the schemas) in document order.
    public override void Operands(OperatorCondition condition, IConditionOperator? @operator, IReadOnlyList<PathNode?> paths,
        string location, ValidationProblems problems)
    {
        if (@operator is null || condition.Operands.Count < @operator.MinOperands || condition.Operands.Count > @operator.MaxOperands)
            return;

        var context = new OperandValidationContext(condition,
            schemaPaths is null ? null : index => paths[index] is { } path ? schemaPaths.Resolve(path, problems) : null);

        // The validator never throws for a rule: an operator whose validation fails is reported at the operator.
        try
        {
            @operator.ValidateOperands(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            problems.Error(location, $"Operator '{condition.Operator}' couldn't validate its operands: {ex.Message}");
            return;
        }

        foreach (var problem in context.Problems)
        {
            var at = problem.Operand is not { } index ? location
                : condition.Operands[index] is PathOperand ? $"{location}.operands[{index}].path"
                : $"{location}.operands[{index}]";

            if (problem.IsWarning)
                problems.Warning(at, problem.Message);
            else
                problems.Error(at, problem.Message);
        }
    }
}

/// <summary>Actions must be registered, and delayed or advanced schedules need an amount and a unit.</summary>
internal sealed class ActionCheck(Catalog catalog) : RuleCheck
{
    public override void Action(ActionDefinition action, string location, ValidationProblems problems)
    {
        if (string.IsNullOrWhiteSpace(action.Action))
            problems.Error($"{location}.action", "'action' must be a non-empty string.");
        else if (!catalog.TryGetAction(action.Action, out _))
            problems.Error($"{location}.action", $"No action is registered under the name '{action.Action}'.");
    }

    public override void Schedule(ScheduleMetadata schedule, string location, ValidationProblems problems)
    {
        if (schedule.Mode == ScheduleMode.Immediate)
            return;

        if (schedule.Amount is null)
            problems.Error($"{location}.schedule.amount", $"'amount' is required for '{schedule.Mode}' schedules.");

        if (schedule.Unit is null)
            problems.Error($"{location}.schedule.unit", $"'unit' is required for '{schedule.Mode}' schedules.");
    }
}

/// <summary>
/// Paths must be valid (<see cref="RulePath"/>): the parser checks this for JSON rules, this for rules built in code.
/// </summary>
internal sealed class PathSyntaxCheck : RuleCheck
{
    public override void Path(PathNode path, ValidationProblems problems)
    {
        if (path.Error is not null)
            problems.Error(path.Location, path.Error);
    }
}

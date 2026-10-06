using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Abstractions.Scheduling;

namespace RuleForge.Validation;

/// <summary>
/// Checks a rule against the registered operators and action types before it is saved or run: unknown operators,
/// operand counts outside an operator's limits, unknown action types, and incomplete schedules.
/// Property paths are not checked, since that needs to know each event's data type.
/// </summary>
public sealed class RuleValidator
{
    private readonly Dictionary<string, IConditionOperator> _operators = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _actionTypes;

    /// <summary>
    /// Creates a validator for the given operators and action factory — the same ones given to the
    /// <c>ConditionEvaluator</c> and <c>RuleEngine</c>.
    /// </summary>
    public RuleValidator(IEnumerable<IConditionOperator> operators, IActionExecutorFactory actionFactory)
    {
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(actionFactory);

        foreach (var @operator in operators)
        {
            if (!_operators.TryAdd(@operator.Name, @operator))
                throw new ArgumentException($"Operator '{@operator.Name}' is registered more than once.", nameof(operators));
        }

        _actionTypes = new HashSet<string>(actionFactory.ActionTypes, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates <paramref name="rule"/> and returns every problem found; never throws for an invalid rule.
    /// </summary>
    public RuleValidationResult Validate(RuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var errors = new List<RuleValidationError>();

        if (string.IsNullOrWhiteSpace(rule.Name))
            errors.Add(new("$.name", "'name' must be a non-empty string."));

        if (string.IsNullOrWhiteSpace(rule.EventType))
            errors.Add(new("$.eventType", "'eventType' must be a non-empty string."));

        ValidateRootCondition(rule.Condition, "$.condition", "The rule's root condition", errors);

        for (var index = 0; index < rule.Actions.Count; index++)
            ValidateAction(rule.Actions[index], $"$.actions[{index}]", errors);

        return new RuleValidationResult(errors);
    }

    private void ValidateAction(ActionDefinition action, string path, List<RuleValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(action.Type))
            errors.Add(new($"{path}.type", "'type' must be a non-empty string."));
        else if (!_actionTypes.Contains(action.Type))
            errors.Add(new($"{path}.type", $"No action executor is registered for action type '{action.Type}'."));

        if (action.Condition is not null)
            ValidateRootCondition(action.Condition, $"{path}.condition", "An action's condition", errors);

        if (action.Schedule is { Mode: not ScheduleMode.Immediate } schedule)
        {
            if (schedule.Amount is null)
                errors.Add(new($"{path}.schedule.amount", $"'amount' is required for '{schedule.Mode}' schedules."));

            if (schedule.Unit is null)
                errors.Add(new($"{path}.schedule.unit", $"'unit' is required for '{schedule.Mode}' schedules."));
        }
    }

    private void ValidateRootCondition(
        ConditionNode? condition, string path, string description, List<RuleValidationError> errors)
    {
        if (condition is OperatorCondition root)
            ValidateOperator(root, path, errors);
        else
            errors.Add(new(path, $"{description} must be an operator, not {condition?.GetType().Name ?? "null"}."));
    }

    private void ValidateOperator(OperatorCondition condition, string path, List<RuleValidationError> errors)
    {
        if (!_operators.TryGetValue(condition.Operator, out var @operator))
        {
            errors.Add(new($"{path}.operator", $"Unknown operator '{condition.Operator}'."));
        }
        else if (condition.Operands.Count < @operator.MinOperands)
        {
            errors.Add(new($"{path}.operands",
                $"Operator '{condition.Operator}' requires at least {@operator.MinOperands} operand(s), but has {condition.Operands.Count}."));
        }
        else if (condition.Operands.Count > @operator.MaxOperands)
        {
            errors.Add(new($"{path}.operands",
                $"Operator '{condition.Operator}' allows at most {@operator.MaxOperands} operand(s), but has {condition.Operands.Count}."));
        }

        for (var index = 0; index < condition.Operands.Count; index++)
        {
            if (condition.Operands[index] is OperatorCondition nested)
                ValidateOperator(nested, $"{path}.operands[{index}]", errors);
        }
    }
}

using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Engine;
using RuleLoom.Operators;
using RuleLoom.Schemas;

namespace RuleLoom.Validation;

/// <summary>
/// Checks a rule against the registered operators and actions before it is saved or run: unknown operators,
/// operand counts outside an operator's limits, unknown actions, and incomplete schedules. With a schema store,
/// <see cref="ValidateAsync"/> also checks paths against the event's schema and its contexts' schemas.
/// </summary>
public sealed class RuleValidator
{
    private readonly Dictionary<string, IConditionOperator> _operators = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _actionNames;
    private readonly HashSet<string> _contextProviderNames;
    private readonly ISchemaStore? _schemas;

    /// <summary>
    /// Creates a validator for the given operators, action factory, context providers and schema store — the same ones
    /// given to the <c>ConditionEvaluator</c> and <c>RuleEngine</c>.
    /// </summary>
    /// <param name="operators">The registered operators.</param>
    /// <param name="actionFactory">The registered actions.</param>
    /// <param name="contextProviders">The registered context providers; contexts without one are errors when paths are
    /// checked.</param>
    /// <param name="schemas">Where event and context schemas are kept; paths are only checked with one.</param>
    public RuleValidator(
        IEnumerable<IConditionOperator> operators,
        IActionFactory actionFactory,
        IContextProviderFactory? contextProviders = null,
        ISchemaStore? schemas = null)
    {
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(actionFactory);

        foreach (var @operator in operators)
        {
            if (!_operators.TryAdd(@operator.Name, @operator))
                throw new ArgumentException($"Operator '{@operator.Name}' is registered more than once.", nameof(operators));
        }

        _actionNames = new HashSet<string>(actionFactory.ActionNames, StringComparer.OrdinalIgnoreCase);
        _contextProviderNames = new HashSet<string>(contextProviders?.ProviderNames ?? [], StringComparer.OrdinalIgnoreCase);
        _schemas = schemas;
    }

    /// <summary>
    /// Validates <paramref name="rule"/> like <see cref="Validate"/> and, when the schema store has a schema for its event,
    /// checks its paths: unknown fields and contexts, contexts without a provider, and paths into lists are errors;
    /// comparisons that can't match and paths into contexts without a schema are warnings. Never throws for an invalid rule.
    /// </summary>
    public async Task<RuleValidationResult> ValidateAsync(RuleDefinition rule, CancellationToken cancellationToken = default)
    {
        var result = Validate(rule);

        if (_schemas is null || string.IsNullOrWhiteSpace(rule.EventType) ||
            await _schemas.GetEventAsync(rule.EventType, cancellationToken).ConfigureAwait(false) is not { } schema)
        {
            return result;
        }

        // The declared contexts the rule uses that have a provider: their schema, or null when the provider has none.
        var used = ContextReferences.Collect([rule]);
        var contexts = new Dictionary<string, ContextSchemaDefinition?>(StringComparer.OrdinalIgnoreCase);
        foreach (var context in schema.Contexts.Where(c => used.Contains(c.Name) && _contextProviderNames.Contains(c.Provider)))
            contexts[context.Name] = await _schemas.GetContextAsync(context.Provider, cancellationToken).ConfigureAwait(false);

        var (errors, warnings) = new SchemaPathChecker(schema, contexts, _operators).Check(rule);

        return new RuleValidationResult([.. result.Errors, .. errors]) { Warnings = warnings };
    }

    /// <summary>
    /// Validates <paramref name="rule"/> and returns every problem found; never throws for an invalid rule.
    /// </summary>
    public RuleValidationResult Validate(RuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var errors = new List<RuleValidationError>();

        if (string.IsNullOrWhiteSpace(rule.Id))
            errors.Add(new("$.id", "'id' must be a non-empty string."));

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
        if (string.IsNullOrWhiteSpace(action.Action))
            errors.Add(new($"{path}.action", "'action' must be a non-empty string."));
        else if (!_actionNames.Contains(action.Action))
            errors.Add(new($"{path}.action", $"No action is registered under the name '{action.Action}'."));

        if (action.Condition is not null)
            ValidateRootCondition(action.Condition, $"{path}.condition", "An action's condition", errors);

        if (action.Schedule?.ReferencePath is { } referencePath)
            ValidatePath(referencePath, $"{path}.schedule.referencePath", errors);

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
            switch (condition.Operands[index])
            {
                case OperatorCondition nested:
                    ValidateOperator(nested, $"{path}.operands[{index}]", errors);
                    break;
                case PathOperand operand:
                    ValidatePath(operand.Path, $"{path}.operands[{index}].path", errors);

                    if (@operator?.GetType() == typeof(ChangedOperator) && operand.Path.StartsWith('@'))
                    {
                        errors.Add(new($"{path}.operands[{index}].path",
                            $"'changed' can't be used on a context ('{operand.Path}'): contexts have no previous state."));
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Checks what the parser checks in paths, for rules built in code: reserved prefixes, variables and contexts after a
    /// state.
    /// </summary>
    private static void ValidatePath(string path, string location, List<RuleValidationError> errors)
    {
        var segments = path.Split('.', 3);
        var first = segments[0];

        var problem = first switch
        {
            _ when first.StartsWith('#') => $"Paths starting with '#' are reserved for functions: '{path}'.",
            "@" => $"A context path needs a name after '@', e.g. '@customer.Tier': '{path}'.",
            _ when !first.StartsWith('$') => null,
            _ when !first.Equals("$current", StringComparison.OrdinalIgnoreCase) && !first.Equals("$previous", StringComparison.OrdinalIgnoreCase) =>
                $"Unknown variable '{first}' in path '{path}'. Use $current or $previous.",
            _ when segments.Length > 1 && segments[1].StartsWith('@') =>
                $"Contexts have no current or previous state; use '{string.Join('.', segments[1..])}' instead of '{path}'.",
            _ => null
        };

        if (problem is not null)
            errors.Add(new(location, problem));
    }
}

using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
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
    private readonly HashSet<string> _contextProviderNames;
    private readonly ISchemaStore? _schemas;
    private readonly IReadOnlyList<RuleCheck> _checks;

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

        _contextProviderNames = new HashSet<string>(contextProviders?.ProviderNames ?? [], StringComparer.OrdinalIgnoreCase);
        _schemas = schemas;
        _checks =
        [
            new RequiredFieldsCheck(),
            new RootConditionCheck(),
            new OperatorCheck(),
            new ActionCheck(new HashSet<string>(actionFactory.ActionNames, StringComparer.OrdinalIgnoreCase)),
            new PathSyntaxCheck()
        ];
    }

    /// <summary>
    /// Validates <paramref name="rule"/> and returns every problem found; never throws for an invalid rule.
    /// </summary>
    public RuleValidationResult Validate(RuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new RuleWalker(_operators, _checks).Walk(rule);
    }

    /// <summary>
    /// Validates <paramref name="rule"/> like <see cref="Validate"/> and, when the schema store has a schema for its event,
    /// checks its paths: unknown fields and contexts, contexts without a provider, and paths into lists are errors;
    /// comparisons that can't match and paths into contexts without a schema are warnings. Never throws for an invalid rule.
    /// </summary>
    public async Task<RuleValidationResult> ValidateAsync(RuleDefinition rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (_schemas is null || string.IsNullOrWhiteSpace(rule.EventType) ||
            await _schemas.GetEventAsync(rule.EventType, cancellationToken).ConfigureAwait(false) is not { } schema)
        {
            return Validate(rule);
        }

        // The declared contexts the rule uses that have a provider: their schema, or null when the provider has none.
        var used = ContextReferences.Collect([rule]);
        var contexts = new Dictionary<string, ContextSchemaDefinition?>(StringComparer.OrdinalIgnoreCase);
        foreach (var context in schema.Contexts.Where(c => used.Contains(c.Name) && _contextProviderNames.Contains(c.Provider)))
            contexts[context.Name] = await _schemas.GetContextAsync(context.Provider, cancellationToken).ConfigureAwait(false);

        var paths = new SchemaPaths(schema, contexts);
        return new RuleWalker(_operators, [.. _checks, new SchemaPathCheck(paths), new ComparisonTypeCheck(paths)]).Walk(rule);
    }
}

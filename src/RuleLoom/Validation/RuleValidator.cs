using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Validation;

/// <summary>
/// Checks a rule against a <see cref="Catalog"/> before it is saved or run: its event type, operators and their operand
/// counts, actions and their parameters, incomplete schedules, and its paths against the event's schema and its contexts'
/// schemas.
/// </summary>
/// <param name="catalog">The catalog the rule is written against, the same one the engine runs on.</param>
public sealed class RuleValidator(Catalog catalog)
{
    private readonly Catalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>
    /// Validates <paramref name="rule"/> and returns every problem found; never throws for an invalid rule. An event type
    /// that isn't in the catalog, unknown fields and contexts (including contexts the event doesn't declare or whose link
    /// is broken), paths into lists, and a typed action's unknown or missing parameters and values of the wrong type are
    /// errors; comparisons that can't match, parameter paths whose fields the action can't read and paths into contexts
    /// without a schema are warnings. Untyped actions' parameters aren't checked.
    /// </summary>
    public RuleValidationResult Validate(RuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var schemaPaths = !string.IsNullOrWhiteSpace(rule.EventType) && _catalog.TryGetEvent(rule.EventType, out var schema)
            ? new SchemaPaths(_catalog, schema)
            : null;

        List<RuleCheck> checks =
        [
            new RequiredFieldsCheck(),
            new EventTypeCheck(_catalog),
            new RootConditionCheck(),
            new ActionCheck(_catalog),
            new PathSyntaxCheck()
        ];

        if (schemaPaths is not null)
            checks.Add(new SchemaPathCheck(schemaPaths));

        checks.Add(new ParameterCheck(_catalog, schemaPaths));

        // Last: operators validate their operands after the generic checks have reported the operands' paths.
        checks.Add(new OperatorCheck(schemaPaths));

        return new RuleWalker(_catalog, checks).Walk(rule);
    }
}

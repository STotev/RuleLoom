namespace RuleLoom.Validation;

/// <summary>
/// The outcome of <see cref="RuleValidator.Validate"/>: every problem found in a rule.
/// </summary>
/// <param name="Errors">The problems that make the rule invalid; empty when it is valid.</param>
public sealed record RuleValidationResult(IReadOnlyList<RuleValidationError> Errors)
{
    /// <summary>
    /// Suspicious parts that don't make the rule invalid, e.g. a comparison that can never be true or paths that can't be
    /// checked.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Warnings { get; init; } = [];

    /// <summary>
    /// True when no problems were found.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// One problem (an error or a warning) found by <see cref="RuleValidator"/>.
/// </summary>
/// <param name="Path">The JSONPath of the problem in the rule, e.g. <c>$.condition.operands[1]</c>.</param>
/// <param name="Message">What is wrong.</param>
public sealed record RuleValidationError(string Path, string Message);

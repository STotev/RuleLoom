namespace RuleForge.Validation;

/// <summary>
/// The outcome of <see cref="RuleValidator.Validate"/>: every problem found in a rule.
/// </summary>
/// <param name="Errors">The problems, in document order; empty when the rule is valid.</param>
public sealed record RuleValidationResult(IReadOnlyList<RuleValidationError> Errors)
{
    /// <summary>
    /// True when no problems were found.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// One problem found by <see cref="RuleValidator"/>.
/// </summary>
/// <param name="Path">The JSONPath of the problem in the rule, e.g. <c>$.condition.operands[1]</c>.</param>
/// <param name="Message">What is wrong.</param>
public sealed record RuleValidationError(string Path, string Message);

namespace RuleLoom.Abstractions.Rule;

/// <summary>
/// The outcome of evaluating a rule's condition.
/// </summary>
public enum RuleExecutionStatus
{
    /// <summary>The condition was true and the rule's actions were processed.</summary>
    Matched,

    /// <summary>The condition was false; no actions were processed.</summary>
    NotMatched,

    /// <summary>The condition could not be evaluated (e.g. an unknown operator); no actions were processed.</summary>
    Failed
}

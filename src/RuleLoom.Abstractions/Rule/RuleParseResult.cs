using System.Diagnostics.CodeAnalysis;

namespace RuleLoom.Abstractions.Rule;

/// <summary>
/// The outcome of parsing one rule of an array with <see cref="RuleJsonParser.ParseEach"/>: the rule, or why it is
/// malformed.
/// </summary>
/// <param name="Index">The rule's index in the array.</param>
/// <param name="Rule">The parsed rule; <c>null</c> when it is malformed.</param>
/// <param name="Error">Why the rule is malformed, with its JSONPath in the array (e.g. <c>$[2].condition</c>);
/// <c>null</c> when it was parsed.</param>
public sealed record RuleParseResult(int Index, RuleDefinition? Rule, RuleParseException? Error)
{
    /// <summary>True when the rule was parsed (<see cref="Rule"/> is set).</summary>
    [MemberNotNullWhen(true, nameof(Rule))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsParsed => Rule is not null;
}

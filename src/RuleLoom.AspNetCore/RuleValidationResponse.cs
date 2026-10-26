using System.Text.Json.Serialization;
using RuleLoom.Validation;

namespace RuleLoom.AspNetCore;

/// <summary>
/// The outcome of validating one rule with <c>POST {prefix}/validate</c>. Paths are relative to the rule
/// (<c>$.condition</c>), also for a rule of an array.
/// </summary>
/// <param name="Index">The rule's index when an array was posted; <c>null</c> for a single rule.</param>
/// <param name="Id">The rule's id; <c>null</c> when the rule couldn't be parsed.</param>
/// <param name="Parsed">Whether the rule could be parsed; a rule that couldn't has one error and wasn't validated.</param>
/// <param name="IsValid">True when there are no errors.</param>
/// <param name="Errors">The parse error, or the validation errors.</param>
/// <param name="Warnings">The validation warnings.</param>
public sealed record RuleValidationResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Index,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id,
    bool Parsed,
    bool IsValid,
    IReadOnlyList<RuleValidationError> Errors,
    IReadOnlyList<RuleValidationError> Warnings);

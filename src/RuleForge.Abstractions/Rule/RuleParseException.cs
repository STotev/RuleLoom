using System.Text.Json;

namespace RuleForge.Abstractions.Rule;

/// <summary>
/// Thrown by <see cref="RuleJsonParser"/> when well-formed JSON doesn't describe a valid rule.
/// <see cref="JsonException.Path"/> holds the JSONPath of the offending element, e.g. <c>$.condition.operands[1]</c>
/// (or <c>$[2].condition</c> for <see cref="RuleJsonParser.ParseMany"/>).
/// </summary>
public sealed class RuleParseException : JsonException
{
    /// <summary>
    /// Creates a new <see cref="RuleParseException"/>.
    /// </summary>
    /// <param name="message">What is wrong with the element.</param>
    /// <param name="path">The JSONPath of the offending element.</param>
    public RuleParseException(string message, string path)
        : base($"{message} (at {path})", path, lineNumber: null, bytePositionInLine: null)
    {
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Abstractions.Values;

/// <summary>
/// Compares rule values with domain values of possibly different CLR types. Used by the built-in operators;
/// custom operators can use it to behave consistently.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Numbers compare by value across numeric types (as <see cref="double"/> if either side is
/// <see cref="float"/>/<see cref="double"/>, otherwise as <see cref="decimal"/>). <c>NaN</c> never matches.</item>
/// <item>Enums match by name (case-insensitive) or underlying number; different enum types never match.</item>
/// <item><see cref="DateTime"/>/<see cref="DateTimeOffset"/> compare as moments, read by the given
/// <see cref="TemporalRules"/> (a date-time without an offset is wall-clock time in its time zone); a string on the
/// other side must be strict ISO 8601.</item>
/// <item>Two strict ISO 8601 strings compare as moments; an ISO date string against any other string can't be
/// compared (not equal, not ordered). Other strings compare ordinally (case-sensitive).</item>
/// <item>JSON objects and arrays (<see cref="JsonElement"/>, <see cref="JsonNode"/>) are equal when their content is
/// (same JSON model on both sides) and are never ordered.</item>
/// <item><c>null</c> equals only <c>null</c> and is never ordered.</item>
/// <item>Otherwise equality uses <see cref="object.Equals(object?)"/>, and ordering uses
/// <see cref="IComparable"/> when both values have the same type.</item>
/// </list>
/// </remarks>
public static class ValueComparer
{
    /// <summary>
    /// Determines whether two values are equal under the comparison rules.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <param name="temporalRules">How date-times are read (the catalog's, or <see cref="TemporalRules.Utc"/>).</param>
    /// <exception cref="InvalidTemporalValueException">A value is a wall-clock time that doesn't exist in its zone.</exception>
    public static bool AreEqual(object? left, object? right, TemporalRules temporalRules)
    {
        ArgumentNullException.ThrowIfNull(temporalRules);

        if (left is null || right is null)
            return left is null && right is null;

        if (left is JsonElement or JsonNode || right is JsonElement or JsonNode)
            return JsonEquals(left, right);

        var comparison = Compare(left, right, temporalRules);
        if (comparison is not null)
            return comparison == 0;

        // Two numbers are only incomparable when one is NaN, which must not equal anything (double.Equals says NaN == NaN).
        return !(ValueConverter.IsNumber(left) && ValueConverter.IsNumber(right)) && left.Equals(right);
    }

    /// <summary>
    /// Tries to order two values under the comparison rules.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <param name="temporalRules">How date-times are read (the catalog's, or <see cref="TemporalRules.Utc"/>).</param>
    /// <param name="result">Negative if <paramref name="left"/> is smaller, zero if equal, positive if greater.</param>
    /// <returns><c>false</c> if the values can't be ordered (e.g. a number and a non-numeric string, or a null).</returns>
    /// <exception cref="InvalidTemporalValueException">A value is a wall-clock time that doesn't exist in its zone.</exception>
    public static bool TryCompare(object? left, object? right, TemporalRules temporalRules, out int result)
    {
        ArgumentNullException.ThrowIfNull(temporalRules);
        result = 0;

        if (left is null || right is null)
            return false;

        var comparison = Compare(left, right, temporalRules);

        // Strings are fully handled by Compare (a date against other text is deliberately incomparable).
        if (comparison is null && left is not string && left.GetType() == right.GetType() && left is IComparable comparable)
            comparison = Math.Sign(comparable.CompareTo(right));

        if (comparison is null)
            return false;

        result = comparison.Value;
        return true;
    }

    private static int? Compare(object left, object right, TemporalRules temporalRules)
    {
        if (ValueConverter.IsNumber(left) && ValueConverter.IsNumber(right))
            return CompareNumbers(left, right);

        if (left is Enum || right is Enum)
            return CompareEnums(left, right, temporalRules);

        if (left is DateTime or DateTimeOffset || right is DateTime or DateTimeOffset)
            return CompareDates(left, right, temporalRules);

        if (left is string leftText && right is string rightText)
            return CompareStrings(leftText, rightText, temporalRules);

        return null;
    }

    private static int? CompareStrings(string left, string right, TemporalRules temporalRules)
    {
        var leftIsDate = temporalRules.TryGetInstant(left, out var leftInstant);
        var rightIsDate = temporalRules.TryGetInstant(right, out var rightInstant);

        if (leftIsDate && rightIsDate)
            return leftInstant.UtcTicks.CompareTo(rightInstant.UtcTicks);

        // A date against arbitrary text can't be compared; two ordinary strings compare ordinally.
        return leftIsDate || rightIsDate ? null : Math.Sign(string.CompareOrdinal(left, right));
    }

    private static bool JsonEquals(object left, object right) => (left, right) switch
    {
        (JsonElement leftElement, JsonElement rightElement) => JsonElement.DeepEquals(leftElement, rightElement),
        (JsonNode leftNode, JsonNode rightNode) => JsonNode.DeepEquals(leftNode, rightNode),
        _ => false
    };

    private static int? CompareNumbers(object left, object right)
    {
        if (left is float or double || right is float or double)
        {
            var leftDouble = Convert.ToDouble(left);
            var rightDouble = Convert.ToDouble(right);

            if (double.IsNaN(leftDouble) || double.IsNaN(rightDouble))
                return null;

            return leftDouble.CompareTo(rightDouble);
        }

        return Convert.ToDecimal(left).CompareTo(Convert.ToDecimal(right));
    }

    private static int? CompareEnums(object left, object right, TemporalRules temporalRules)
    {
        if (left is Enum && right is Enum)
        {
            return left.GetType() == right.GetType()
                ? CompareNumbers(UnderlyingValue(left), UnderlyingValue(right))
                : null;
        }

        var enumType = left is Enum ? left.GetType() : right.GetType();
        var other = left is Enum ? right : left;

        if (!ValueConverter.TryConvert(other, enumType, temporalRules, out var converted))
            return null;

        return left is Enum
            ? CompareNumbers(UnderlyingValue(left), UnderlyingValue(converted!))
            : CompareNumbers(UnderlyingValue(converted!), UnderlyingValue(right));
    }

    private static int? CompareDates(object left, object right, TemporalRules temporalRules)
    {
        if (!temporalRules.TryGetInstant(left, out var leftInstant) || !temporalRules.TryGetInstant(right, out var rightInstant))
        {
            return null;
        }

        return leftInstant.UtcTicks.CompareTo(rightInstant.UtcTicks);
    }

    private static object UnderlyingValue(object enumValue) =>
        Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()));
}

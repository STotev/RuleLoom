using RuleLoom.Abstractions.Values;
using RuleLoom.Schemas;

namespace RuleLoom;

/// <summary>How field types read in messages.</summary>
internal static class FieldTypes
{
    public static string Describe(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.Number => "a number",
        FieldType.String => "text",
        FieldType.Boolean => "a boolean",
        FieldType.DateTime => "a date and time",
        FieldType.Date => "a date",
        FieldType.Enum => "an enum",
        FieldType.Object => "an object",
        FieldType.Array => "a list",
        _ => "any value"
    };

    /// <summary>
    /// Whether text is a strict ISO 8601 date or date-time: its form only. UTC never skips a wall-clock time, so this
    /// never throws; whether the time exists in the catalog's time zone is a separate check.
    /// </summary>
    public static bool IsDateTimeText(string? text) => text is not null && TemporalRules.Utc.TryGetInstant(text, out _);
}

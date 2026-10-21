using System.Collections;
using System.Globalization;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Values;
using RuleLoom.Schemas;
using RuleLoom.Validation;

namespace RuleLoom.Operators;

/// <summary>
/// Operand validation shared by the comparison operators: with schemas, warns when the two operands' types can never
/// match under <see cref="ValueComparer"/>, or when an ordering operator gets a list or an object (at most one warning).
/// </summary>
internal static class ComparedOperands
{
    public static void Validate(OperandValidationContext context, bool ordering)
    {
        var (leftField, rightField) = (context.GetField(0), context.GetField(1));
        var (left, right) = (context.GetOperand(0), context.GetOperand(1));

        var problem = (leftField, rightField, left, right) switch
        {
            _ when ordering && new[] { leftField, rightField }.FirstOrDefault(f => f?.Type is FieldType.Object or FieldType.Array) is { } unordered =>
                $"can't order {FieldTypes.Describe(unordered.Type)}",
            ({ } l, { } r, _, _) => Compatible(l, r) ? null : $"compares {FieldTypes.Describe(l.Type)} with {FieldTypes.Describe(r.Type)}",
            ({ } l, null, _, ValueOperand value) => Compatible(l, value.Value) ? null : $"compares {FieldTypes.Describe(l.Type)} with {Show(value.Value)}",
            (null, { } r, ValueOperand value, _) => Compatible(r, value.Value) ? null : $"compares {Show(value.Value)} with {FieldTypes.Describe(r.Type)}",
            _ => null
        };

        if (problem is not null)
            context.Warning($"'{context.OperatorName}' {problem}; the condition is never true.");
    }

    /// <summary>
    /// Whether a value can match a field's values under <see cref="ValueComparer"/>. Enums aren't judged: CLR enums match
    /// by C# name or number, JSON state by the serialized text, and the schema can't tell which the rule runs on.
    /// </summary>
    private static bool Compatible(FieldDefinition field, object? value) => value is null || field.Type switch
    {
        FieldType.Integer or FieldType.Number => IsNumber(value) || value is Enum,
        FieldType.Boolean => value is bool,
        FieldType.String => value is string or DateTime or DateTimeOffset or Enum,
        FieldType.DateTime or FieldType.Date => value is DateTime or DateTimeOffset ||
                                                value is string text && ValueConverter.TryConvert<DateTimeOffset>(text, out _),
        _ => true
    };

    private static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool Compatible(FieldDefinition left, FieldDefinition right) =>
        Kind(left.Type) is not { } l || Kind(right.Type) is not { } r || l == r;

    // Field types that compare with each other; null when not checked (text can hold dates, numbers as text or enum
    // names; objects and lists compare by content).
    private static string? Kind(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.Number => "number",
        FieldType.Boolean => "boolean",
        FieldType.DateTime or FieldType.Date => "date",
        _ => null
    };

    private static string Show(object? value) => value switch
    {
        string text => $"the text '{text}'",
        bool flag => flag ? "true" : "false",
        IDictionary => "an object",
        IEnumerable => "a list",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => $"{value}"
    };
}

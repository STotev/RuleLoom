using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RuleLoom.Abstractions.Values;

/// <summary>
/// Converts rule values (typically parsed from JSON) to the CLR types used by domain objects,
/// following the same rules as <see cref="ValueComparer"/>.
/// </summary>
/// <remarks>
/// Supported conversions: between numeric types when no value is lost; enum names (case-insensitive)
/// and integral numbers to enums; between <see cref="DateTime"/> and <see cref="DateTimeOffset"/>,
/// and strict ISO 8601 strings to either, read by the given <see cref="TemporalRules"/> (a <see cref="DateTime"/>
/// result is in UTC).
/// <c>null</c> converts to reference and <see cref="Nullable{T}"/> types; values already assignable
/// to the target are returned unchanged.
/// </remarks>
public static class ValueConverter
{
    /// <summary>
    /// Tries to convert <paramref name="value"/> to <paramref name="targetType"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <param name="temporalRules">How date-times are read (the catalog's, or <see cref="TemporalRules.Utc"/>).</param>
    /// <param name="result">The converted value.</param>
    /// <returns><c>true</c> if the value was converted; otherwise <c>false</c>.</returns>
    /// <exception cref="InvalidTemporalValueException">The value is a wall-clock time that doesn't exist in its zone.</exception>
    public static bool TryConvert(object? value, Type targetType, TemporalRules temporalRules, out object? result)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(temporalRules);

        result = null;
        var underlying = Nullable.GetUnderlyingType(targetType);

        if (value is null)
            return !targetType.IsValueType || underlying is not null;

        var target = underlying ?? targetType;

        if (target.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        if (target.IsEnum)
            return TryConvertToEnum(value, target, out result);

        if (target == typeof(DateTimeOffset) || target == typeof(DateTime))
        {
            if (!temporalRules.TryGetInstant(value, out var instant))
                return false;

            result = target == typeof(DateTime) ? (object)instant.UtcDateTime : instant;
            return true;
        }

        if (IsNumber(value) && IsNumericType(target))
            return TryConvertNumber(value, target, out result);

        return false;
    }

    /// <summary>
    /// Tries to convert <paramref name="value"/> to <typeparamref name="T"/>.
    /// </summary>
    /// <returns><c>true</c> if the value was converted; otherwise <c>false</c>.</returns>
    /// <exception cref="InvalidTemporalValueException">The value is a wall-clock time that doesn't exist in its zone.</exception>
    public static bool TryConvert<T>(object? value, TemporalRules temporalRules, [MaybeNullWhen(false)] out T result)
    {
        if (TryConvert(value, typeof(T), temporalRules, out var converted))
        {
            result = (T)converted!;
            return true;
        }

        result = default;
        return false;
    }

    internal static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool IsNumericType(Type type) =>
        type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool TryConvertNumber(object value, Type target, out object? result)
    {
        result = null;

        try
        {
            var converted = Convert.ChangeType(value, target, CultureInfo.InvariantCulture);

            // Lossless only: converting back must give the original value (rejects dropped fractions and precision).
            var roundTrip = Convert.ChangeType(converted, value.GetType(), CultureInfo.InvariantCulture);
            if (!value.Equals(roundTrip))
                return false;

            result = converted;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryConvertToEnum(object value, Type enumType, out object? result)
    {
        result = null;

        if (value is string name)
        {
            // Names only: numeric strings would otherwise be accepted by Enum.TryParse.
            var trimmed = name.TrimStart();
            if (trimmed.Length == 0 || char.IsDigit(trimmed[0]) || trimmed[0] is '-' or '+')
                return false;

            if (!Enum.TryParse(enumType, name, ignoreCase: true, out var parsed))
                return false;

            result = parsed;
            return true;
        }

        if (IsNumber(value) && TryConvertNumber(value, Enum.GetUnderlyingType(enumType), out var raw))
        {
            result = Enum.ToObject(enumType, raw!);
            return true;
        }

        return false;
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RuleForge.Abstractions.Values;

/// <summary>
/// Converts rule values (typically parsed from JSON) to the CLR types used by domain objects,
/// following the same rules as <see cref="ValueComparer"/>.
/// </summary>
/// <remarks>
/// Supported conversions: between numeric types when no value is lost; enum names (case-insensitive)
/// and integral numbers to enums; between <see cref="DateTime"/> and <see cref="DateTimeOffset"/>,
/// and strict ISO 8601 strings to either (no offset means UTC, date-only means UTC midnight).
/// <c>null</c> converts to reference and <see cref="Nullable{T}"/> types; values already assignable
/// to the target are returned unchanged.
/// </remarks>
public static class ValueConverter
{
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    /// <summary>
    /// Tries to convert <paramref name="value"/> to <paramref name="targetType"/>.
    /// </summary>
    /// <returns><c>true</c> if the value was converted; otherwise <c>false</c>.</returns>
    public static bool TryConvert(object? value, Type targetType, out object? result)
    {
        ArgumentNullException.ThrowIfNull(targetType);

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
            if (!TryConvertToDateTimeOffset(value, out var instant))
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
    public static bool TryConvert<T>(object? value, [MaybeNullWhen(false)] out T result)
    {
        if (TryConvert(value, typeof(T), out var converted))
        {
            result = (T)converted!;
            return true;
        }

        result = default;
        return false;
    }

    internal static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    internal static bool TryConvertToDateTimeOffset(object value, out DateTimeOffset result)
    {
        switch (value)
        {
            case DateTimeOffset dateTimeOffset:
                result = dateTimeOffset.ToUniversalTime();
                return true;
            case DateTime dateTime:
                result = dateTime.Kind == DateTimeKind.Local
                    ? new DateTimeOffset(dateTime.ToUniversalTime())
                    : new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                return true;
            case string text:
                return DateTimeOffset.TryParseExact(
                    text,
                    IsoFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out result);
            default:
                result = default;
                return false;
        }
    }

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

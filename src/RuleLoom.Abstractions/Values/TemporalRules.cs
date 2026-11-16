using System.Globalization;

namespace RuleLoom.Abstractions.Values;

/// <summary>
/// How date-times are read: the one place that decides time-zone questions. A date-time that states its offset is that
/// moment; one that doesn't (<see cref="DateTimeKind.Unspecified"/>, ISO 8601 text without an offset) is wall-clock time
/// in <see cref="TimeZone"/>; <see cref="DateTimeKind.Local"/> is wall-clock time on this machine.
/// </summary>
/// <remarks>
/// A wall-clock time that doesn't exist in its zone (skipped when the clocks move forward) is invalid and throws
/// <see cref="InvalidTemporalValueException"/>. One that occurs twice (when the clocks move back) is read as the earlier
/// occurrence; a <see cref="DateTimeKind.Local"/> value keeps .NET's own conversion, which preserves an occurrence it
/// already knows and otherwise assumes standard time. <see cref="DateTimeKind.Local"/> depends on the machine's time
/// zone, so avoid it when rules run on several machines.
/// </remarks>
public sealed class TemporalRules
{
    private static readonly string[] WallClockFormats =
    [
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"
    ];

    private static readonly string[] OffsetFormats =
    [
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Date-times without an offset are UTC: the default when no time zone is configured.</summary>
    public static TemporalRules Utc { get; } = new(TimeZoneInfo.Utc);

    /// <summary>Creates the rules for <paramref name="timeZone"/>.</summary>
    public TemporalRules(TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        TimeZone = timeZone;
    }

    /// <summary>The time zone date-times without an offset are read in.</summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>
    /// Tries to read <paramref name="value"/> as a calendar date: a <see cref="DateOnly"/>, or strict ISO 8601 date-only
    /// text (<c>2026-05-01</c>). A date is a day, never a moment; date-times aren't dates (see <see cref="GetDate"/>).
    /// </summary>
    public bool TryGetDate(object? value, out DateOnly date)
    {
        switch (value)
        {
            case DateOnly day:
                date = day;
                return true;
            case string text when text.Length == 10 && DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                date = parsed;
                return true;
            default:
                date = default;
                return false;
        }
    }

    /// <summary>The calendar date of <paramref name="instant"/> in <see cref="TimeZone"/>.</summary>
    public DateOnly GetDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    /// <summary>
    /// The moment <paramref name="date"/> starts in <see cref="TimeZone"/>; when the clocks skip midnight that day, the
    /// first moment after the gap.
    /// </summary>
    public DateTimeOffset GetStartOfDay(DateOnly date) => Lenient(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// Tries to read <paramref name="value"/> as a moment: a <see cref="DateTimeOffset"/>, a <see cref="DateTime"/>, or
    /// strict ISO 8601 text (<c>2026-05-01T09:00</c>, with seconds, fractions and an offset or <c>Z</c> optional). Asked
    /// for a moment, a date (a <see cref="DateOnly"/> or <c>2026-05-01</c>) is the start of its day
    /// (<see cref="GetStartOfDay"/>); comparisons keep dates as days.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="instant">The moment, in UTC.</param>
    /// <returns><c>false</c> when the value isn't a date-time (e.g. <c>true</c> or other text).</returns>
    /// <exception cref="InvalidTemporalValueException">The value is a wall-clock time that doesn't exist in its zone.</exception>
    public bool TryGetInstant(object? value, out DateTimeOffset instant)
    {
        switch (value)
        {
            case DateTimeOffset offset:
                instant = offset.ToUniversalTime();
                return true;
            case DateTime dateTime:
                instant = Resolve(dateTime, value);
                return true;
            case DateOnly date:
                instant = GetStartOfDay(date);
                return true;
            case string text:
                return TryParse(text, out instant);
            default:
                instant = default;
                return false;
        }
    }

    private bool TryParse(string text, out DateTimeOffset instant)
    {
        instant = default;

        // Cheap shape check (yyyy-...) so ordinary text skips the parsing.
        if (text.Length < 10 || !char.IsAsciiDigit(text[0]) || text[4] != '-')
            return false;

        if (TryGetDate(text, out var date))
        {
            instant = GetStartOfDay(date);
            return true;
        }

        if (DateTime.TryParseExact(text, WallClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock))
        {
            instant = InZone(wallClock, TimeZone, text);
            return true;
        }

        if (DateTimeOffset.TryParseExact(text, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var offset))
        {
            instant = offset.ToUniversalTime();
            return true;
        }

        return false;
    }

    private DateTimeOffset Resolve(DateTime dateTime, object value) => dateTime.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(dateTime),
        DateTimeKind.Local => TimeZoneInfo.Local.IsInvalidTime(dateTime)
            ? throw new InvalidTemporalValueException(Format(value), TimeZoneInfo.Local.Id)
            : new DateTimeOffset(dateTime.ToUniversalTime()),
        _ => InZone(dateTime, TimeZone, value)
    };

    // Wall-clock time in a zone: a skipped time is invalid; a repeated one is the earlier occurrence (the larger offset).
    private static DateTimeOffset InZone(DateTime wallClock, TimeZoneInfo zone, object value)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(unspecified))
            throw new InvalidTemporalValueException(Format(value), zone.Id);

        var offset = zone.IsAmbiguousTime(unspecified)
            ? zone.GetAmbiguousTimeOffsets(unspecified).Max()
            : zone.GetUtcOffset(unspecified);

        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    private DateTimeOffset Lenient(DateTime wallClock)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        // A skipped time moves forward by the gap: read it with the offset from just before the gap.
        if (TimeZone.IsInvalidTime(unspecified))
            return new DateTimeOffset(unspecified, TimeZone.GetUtcOffset(unspecified.AddHours(-12))).ToUniversalTime();

        return InZone(unspecified, TimeZone, wallClock);
    }

    /// <summary>A value as <see cref="InvalidTemporalValueException.Value"/> shows it.</summary>
    internal static string Format(object value) => value switch
    {
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}

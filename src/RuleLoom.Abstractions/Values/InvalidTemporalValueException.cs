namespace RuleLoom.Abstractions.Values;

/// <summary>
/// Thrown by <see cref="TemporalRules"/> for a wall-clock date-time that doesn't exist in its time zone (skipped when the
/// clocks move forward, e.g. <c>02:30</c> on the night daylight saving starts). Such a value can only come from wrong
/// data, so it fails the rule or action that reads it instead of shifting silently. A value that isn't a date-time at
/// all isn't invalid: it just can't be compared or converted.
/// </summary>
public sealed class InvalidTemporalValueException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="value">The value as written or read (e.g. <c>2026-03-29T03:30:00</c>).</param>
    /// <param name="timeZone">The id of the time zone it doesn't exist in.</param>
    /// <param name="path">Where it was read from (e.g. <c>PaymentDueAt</c>), when known.</param>
    public InvalidTemporalValueException(string value, string timeZone, string? path = null)
        : base(Describe(value, timeZone, path is null ? null : $"'{path}'"))
    {
        Value = value;
        TimeZone = timeZone;
        Path = path;
    }

    private InvalidTemporalValueException(InvalidTemporalValueException inner, string? path, string source)
        : base(Describe(inner.Value, inner.TimeZone, source))
    {
        Value = inner.Value;
        TimeZone = inner.TimeZone;
        Path = path;
    }

    /// <summary>The value as written or read.</summary>
    public string Value { get; }

    /// <summary>The id of the time zone the value doesn't exist in.</summary>
    public string TimeZone { get; }

    /// <summary>The path the value was read from (e.g. <c>PaymentDueAt</c>), when known.</summary>
    public string? Path { get; }

    /// <summary>The same problem, read from <paramref name="path"/>; unchanged when its source is already known.</summary>
    internal InvalidTemporalValueException At(string path) => Path is null ? new(this, path, $"'{path}'") : this;

    /// <summary>The same problem, described as coming from <paramref name="source"/> (e.g. a parameter).</summary>
    internal InvalidTemporalValueException From(string source) => Path is null ? new(this, null, source) : this;

    private static string Describe(string value, string timeZone, string? source) =>
        (source is null ? $"'{value}'" : $"{source} is '{value}', which") +
        $" doesn't exist in time zone '{timeZone}': the clocks skip that time.";
}

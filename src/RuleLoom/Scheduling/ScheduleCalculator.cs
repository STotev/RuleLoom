using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Abstractions.Values;

namespace RuleLoom.Scheduling;

internal static class ScheduleCalculator
{
    public static DateTimeOffset Calculate(
        ScheduleMetadata schedule,
        EvaluationContext context,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(context);

        var referenceTime = ResolveReferenceTime(schedule, context, utcNow.ToUniversalTime());
        var (amount, unit) = Offset(schedule);

        var signed = schedule.Mode switch
        {
            ScheduleMode.Delay => amount,
            ScheduleMode.Advance => -amount,
            _ => throw new ArgumentException($"Cannot calculate schedule for mode '{schedule.Mode}'.", nameof(schedule))
        };

        // Days are calendar days in the catalog's time zone (the wall-clock time is kept); other units are elapsed time.
        return unit switch
        {
            ScheduleUnit.Seconds => referenceTime.AddSeconds(signed),
            ScheduleUnit.Minutes => referenceTime.AddMinutes(signed),
            ScheduleUnit.Hours => referenceTime.AddHours(signed),
            ScheduleUnit.Days => context.TemporalRules.AddDays(referenceTime, signed),
            _ => throw new ArgumentException($"Unknown schedule unit '{unit}'.", nameof(schedule))
        };
    }

    private static DateTimeOffset ResolveReferenceTime(
        ScheduleMetadata schedule,
        EvaluationContext context,
        DateTimeOffset utcNow)
    {
        if (schedule.ReferencePath is null)
            return utcNow;

        var resolved = context.Resolve(schedule.ReferencePath);

        if (resolved is null)
        {
            throw new InvalidOperationException(
                $"ReferencePath '{schedule.ReferencePath}' resolved to null. Ensure the property exists and has a value.");
        }

        // Read like rule comparisons: by the catalog's TemporalRules; strings as strict ISO 8601.
        bool isDateTime;
        DateTimeOffset instant;
        try
        {
            isDateTime = context.TemporalRules.TryGetInstant(resolved, out instant);
        }
        catch (InvalidTemporalValueException ex) when (ex.Path is null)
        {
            throw new InvalidTemporalValueException(ex.Value, ex.TimeZone, schedule.ReferencePath);
        }

        if (isDateTime)
            return instant;

        throw new InvalidOperationException(resolved is string text
            ? $"ReferencePath '{schedule.ReferencePath}' resolved to string '{text}', which is not an ISO 8601 date-time " +
              "(e.g. 2026-05-01T12:00:00Z, 2026-05-01T14:00:00+02:00 or 2026-05-01)."
            : $"ReferencePath '{schedule.ReferencePath}' resolved to '{resolved.GetType().Name}' but a DateTime, " +
              "DateTimeOffset or ISO 8601 string is required.");
    }

    private static (int Amount, ScheduleUnit Unit) Offset(ScheduleMetadata schedule)
    {
        var amount = schedule.Amount ?? throw new InvalidOperationException(
            $"Schedule.Amount is required for '{schedule.Mode}' mode.");

        var unit = schedule.Unit ?? throw new InvalidOperationException($"Schedule.Unit is required for '{schedule.Mode}' mode.");

        return (amount, unit);
    }
}

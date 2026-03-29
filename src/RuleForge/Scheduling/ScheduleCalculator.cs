using System.Globalization;
using RuleForge.Abstractions;
using RuleForge.Abstractions.Scheduling;

namespace RuleForge.Scheduling;

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
        var offset = ComputeOffset(schedule);

        return schedule.Mode switch
        {
            ScheduleMode.Delay => referenceTime + offset,
            ScheduleMode.Advance => referenceTime - offset,
            _ => throw new ArgumentException($"Cannot calculate schedule for mode '{schedule.Mode}'.", nameof(schedule))
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

        return resolved switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => ResolveDateTime(dt),
            string dt => ParseDateTimeOffset(dt, schedule.ReferencePath),
            null => throw new InvalidOperationException(
                $"ReferencePath '{schedule.ReferencePath}' resolved to null. Ensure the property exists and has a value."),
            _ => throw new InvalidOperationException(
                $"ReferencePath '{schedule.ReferencePath}' resolved to '{resolved.GetType().Name}' but a DateTime, DateTimeOffset or a stringified datetime value is required.")
        };
    }

    private static TimeSpan ComputeOffset(ScheduleMetadata schedule)
    {
        var amount = schedule.Amount ?? throw new InvalidOperationException(
            $"Schedule.Amount is required for '{schedule.Mode}' mode.");

        if (schedule.Unit is null)
            throw new InvalidOperationException($"Schedule.Unit is required for '{schedule.Mode}' mode.");

        return schedule.Unit.Value switch
        {
            ScheduleUnit.Seconds => TimeSpan.FromSeconds(amount),
            ScheduleUnit.Minutes => TimeSpan.FromMinutes(amount),
            ScheduleUnit.Hours => TimeSpan.FromHours(amount),
            ScheduleUnit.Days => TimeSpan.FromDays(amount),
            _ => throw new ArgumentException($"Unknown schedule unit '{schedule.Unit}'.", nameof(schedule))
        };
    }

    private static DateTimeOffset ResolveDateTime(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Local => new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero),
            DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero)
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(string value, string? referencePath)
    {
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces
                                    | DateTimeStyles.AssumeUniversal
                                    | DateTimeStyles.AdjustToUniversal;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out var parsed))
            return parsed;

        throw new InvalidOperationException(
            $"ReferencePath '{referencePath}' resolved to string '{value}' which could not be parsed as an invariant UTC date-time value.");
    }
}

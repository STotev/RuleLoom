using RuleForge.Abstractions;
using RuleForge.Abstractions.Scheduling;
using RuleForge.Abstractions.Values;

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

        if (resolved is null)
        {
            throw new InvalidOperationException(
                $"ReferencePath '{schedule.ReferencePath}' resolved to null. Ensure the property exists and has a value.");
        }

        // Same conversion as rule comparisons: DateTime/DateTimeOffset as UTC instants, strings as strict ISO 8601.
        if (ValueConverter.TryConvert<DateTimeOffset>(resolved, out var instant))
            return instant.ToUniversalTime();

        throw new InvalidOperationException(resolved is string text
            ? $"ReferencePath '{schedule.ReferencePath}' resolved to string '{text}', which is not an ISO 8601 date-time " +
              "(e.g. 2026-05-01T12:00:00Z, 2026-05-01T14:00:00+02:00 or 2026-05-01)."
            : $"ReferencePath '{schedule.ReferencePath}' resolved to '{resolved.GetType().Name}' but a DateTime, " +
              "DateTimeOffset or ISO 8601 string is required.");
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
}

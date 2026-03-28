namespace RuleForge.Abstractions.Scheduling;

/// <summary>
/// Defines scheduling metadata for an action.
/// </summary>
public sealed record ScheduleMetadata
{
    /// <summary>
    /// Scheduling mode. Defaults to immediate execution.
    /// </summary>
    public ScheduleMode Mode { get; init; } = ScheduleMode.Immediate;

    /// <summary>
    /// Amount to apply for delay/advance schedules.
    /// Null for immediate schedules.
    /// </summary>
    public int? Amount { get; init; }

    /// <summary>
    /// Time unit for delay/advance schedules.
    /// Null for immediate schedules.
    /// </summary>
    public ScheduleUnit? Unit { get; init; }

    /// <summary>
    /// Optional path that resolves to a reference DateTime.
    /// If null, event reception time is used as the reference.
    /// </summary>
    public string? ReferencePath { get; init; }
}

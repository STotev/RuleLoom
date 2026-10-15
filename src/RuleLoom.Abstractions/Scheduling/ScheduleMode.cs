namespace RuleLoom.Abstractions.Scheduling;

/// <summary>
/// Defines when an action should be executed.
/// </summary>
public enum ScheduleMode
{
    Immediate,
    Delay,
    Advance
}

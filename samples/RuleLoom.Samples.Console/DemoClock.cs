namespace RuleLoom.Samples.Console;

/// <summary>
/// A clock the scenarios move by hand, so scheduled actions come due without waiting. RuleLoom takes any
/// <see cref="TimeProvider"/>; an app passes the system clock (the default).
/// </summary>
public sealed class DemoClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

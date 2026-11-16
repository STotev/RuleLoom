namespace RuleLoom.Tests;

/// <summary>A clock stopped at <paramref name="utcNow"/>, so schedules are deterministic.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

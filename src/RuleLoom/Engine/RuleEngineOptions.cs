namespace RuleLoom.Engine;

/// <summary>
/// How a <see cref="RuleEngine"/> runs rules (as opposed to the catalog, which says how they're read). Pass them to the
/// engine's constructor, or configure them with <c>AddRuleLoom(r =&gt; r.Configure(o =&gt; ...))</c>.
/// </summary>
public sealed class RuleEngineOptions
{
    private TimeSpan _overdueGracePeriod;

    /// <summary>
    /// Whether to skip an action whose due time passed by more than <see cref="OverdueGracePeriod"/>: when the event is
    /// processed (its due time was already in the past) and when <see cref="RuleEngine.ExecuteScheduledAsync"/> runs it
    /// late (e.g. a scheduler that was down). Off by default: such an action runs immediately.
    /// </summary>
    public bool SkipOverdueActions { get; set; }

    /// <summary>
    /// How late an action may run and still count as on time, since schedulers don't fire exactly on time (a polling
    /// job, a restart); 0 by default. Only used with <see cref="SkipOverdueActions"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan OverdueGracePeriod
    {
        get => _overdueGracePeriod;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _overdueGracePeriod = value;
        }
    }
}

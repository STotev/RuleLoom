namespace BusinessRules.Abstractions;

public class EventContext
{
    public string EventName { get; init; } = null!;
    public string? TenantId { get; init; }

    public object Current { get; init; } = null!;
    public object? Previous { get; init; }

    public IDictionary<string, object>? Metadata { get; init; }
}
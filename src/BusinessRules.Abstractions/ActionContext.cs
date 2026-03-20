namespace BusinessRules.Abstractions;

public class ActionContext
{
    public EventContext Event { get; init; } = null!;
    public Dictionary<string, object>? Data { get; init; }
}
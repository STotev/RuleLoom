namespace BusinessRules.Abstractions;

public class EvaluationContext
{
    public object Current { get; init; } = null!;
    public object? Previous { get; init; }
}
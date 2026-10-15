namespace RuleLoom.Samples.Console;

/// <summary>
/// A web-shop order; the dependency injection example runs against it.
/// </summary>
public sealed record Order
{
    public required string Number { get; init; }
    public required string CustomerEmail { get; init; }
    public decimal Total { get; init; }
    public OrderStatus Status { get; init; }
}

public enum OrderStatus
{
    Placed,
    Shipped,
    Delivered
}

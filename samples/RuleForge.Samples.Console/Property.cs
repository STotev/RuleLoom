namespace RuleForge.Samples.Console;

/// <summary>
/// The domain object the sample rules run against: a property listing in a real-estate app.
/// </summary>
public sealed record Property
{
    public required string Address { get; init; }
    public decimal Price { get; init; }
    public decimal EstimatedPrice { get; init; }
    public ListingStatus Status { get; init; }
    public DateTimeOffset? ViewingAt { get; init; }
}

public enum ListingStatus
{
    Draft,
    Active,
    Sold
}

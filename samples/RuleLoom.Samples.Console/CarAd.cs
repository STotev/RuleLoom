namespace RuleLoom.Samples.Console;

/// <summary>
/// An advertisement on a used-car marketplace; the basic and JSON examples run against it.
/// </summary>
public sealed record CarAd
{
    public required string Title { get; init; }
    public decimal Price { get; init; }
    public decimal MarketValue { get; init; }
    public AdStatus Status { get; init; }
}

public enum AdStatus
{
    Draft,
    Active,
    Sold
}

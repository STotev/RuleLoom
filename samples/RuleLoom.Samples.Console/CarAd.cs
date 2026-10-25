using RuleLoom.Schemas;

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

/// <summary>
/// The event a car ad is published with: what rules on it can use.
/// </summary>
public sealed class CarAdPublishedSchema : EventSchema<CarAd>
{
    public CarAdPublishedSchema() => Event("CarAdPublished").Label("Car ad published");
}

public enum AdStatus
{
    Draft,
    Active,
    Sold
}

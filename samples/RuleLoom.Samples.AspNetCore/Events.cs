using RuleLoom.Schemas;

namespace RuleLoom.Samples.AspNetCore;

// The events this app raises (sent as JSON in POST /events) and what rules on them can use. Rules on other event types
// are rejected by the validator.

public sealed record Person(string Name, string Email);

public sealed record Order(string Number, string Status, decimal Total, int ItemCount, DateTimeOffset? PaymentDueAt, Person Customer);

public sealed record Ad(string Title, string Status, decimal Price, Person Seller);

public sealed class OrderPlacedSchema : EventSchema<Order>
{
    public OrderPlacedSchema() => Event("OrderPlaced").Label("Order placed");
}

public sealed class OrderUpdatedSchema : EventSchema<Order>
{
    public OrderUpdatedSchema() => Event("OrderUpdated").Label("Order updated");
}

public sealed class AdPublishedSchema : EventSchema<Ad>
{
    public AdPublishedSchema() => Event("AdPublished").Label("Ad published");
}

public sealed class AdUpdatedSchema : EventSchema<Ad>
{
    public AdUpdatedSchema() => Event("AdUpdated").Label("Ad updated");
}
